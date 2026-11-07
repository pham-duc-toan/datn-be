using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Internal;
using Crowd.Contracts.Grpc.Quality;
using Crowd.Labeling;
using Crowd.Project.Api.Exceptions;
using Crowd.Project.Domain.Annotations;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace Crowd.Project.Api.Helpers
{
    /// <summary>Mot mau can gop: nhan DA CHOT theo thu tu nop + thong tin mau.</summary>
    public sealed class MauCanGop
    {
        public MauCanGop(Guid sampleId, SampleMetadata? thongTin, IReadOnlyList<LabelAnnotation> nhan)
        {
            SampleId = sampleId;
            ThongTin = thongTin;
            Nhan = nhan;
        }

        public Guid SampleId { get; }

        public SampleMetadata? ThongTin { get; }

        public IReadOnlyList<LabelAnnotation> Nhan { get; }
    }

    /// <summary>
    /// Gop nhan qua gRPC LabelAggregation cua quality-svc (kien truc 3.24): thuat toan gop CHI con o quality
    /// (app/gop.py) — truoc day project-svc chay ban C# rieng, giu khop bang bo vi du gop.vectors.json. Nguong do
    /// project-svc dua (cung la dau van tay cua bang dem sample_results) nen cung dau vao luon ra cung ket qua.
    ///
    /// Chia lo theo kich thuoc (~2 MB moi lan) — mat na / chuoi khung dai khong vuot gioi han message.
    /// quality-svc tat / qua han → DichVuKhongSanSangException (503): mau nao da co trong bang dem van xem duoc
    /// (AnnotationService chi hoi mau doi).
    /// </summary>
    public sealed class GopNhanClient
    {
        /// <summary>Kich thuoc toi da (ky tu JSON) moi lo gui di.</summary>
        public const int CoLo = 2 * 1024 * 1024;

        private static readonly TimeSpan HetGio = TimeSpan.FromSeconds(60);

        private readonly LabelAggregation.LabelAggregationClient _client;
        private readonly InternalApiOptions _options;
        private readonly TimeProvider _clock;

        public GopNhanClient(LabelAggregation.LabelAggregationClient client, IOptions<InternalApiOptions> options, TimeProvider clock)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _client = client;
            _options = options.Value;
            _clock = clock;
        }

        /// <summary>Ket qua gop theo tung cong cu cua moi mau (sampleId → tools).</summary>
        public async Task<Dictionary<Guid, JsonObject>> GopAsync(
            LabelSchema tapNhan, IReadOnlyList<MauCanGop> mau, NguongKhop nguong, double f1ToiThieu, CancellationToken ct)
        {
            if (tapNhan == null)
            {
                throw new ArgumentNullException(nameof(tapNhan));
            }

            if (mau == null)
            {
                throw new ArgumentNullException(nameof(mau));
            }

            if (nguong == null)
            {
                throw new ArgumentNullException(nameof(nguong));
            }

            Dictionary<Guid, JsonObject> kq = new Dictionary<Guid, JsonObject>();
            string tapNhanJson = tapNhan.ToRawJson().Json;

            AggregateRequest? lo = null;
            int coLo = 0;
            foreach (MauCanGop m in mau)
            {
                SampleLabels dong = new SampleLabels
                {
                    SampleId = m.SampleId.ToString(),
                    MetadataJson = (m.ThongTin ?? SampleMetadata.Rong).ToRawJson().Json,
                };
                int coDong = dong.MetadataJson.Length;
                foreach (LabelAnnotation a in m.Nhan)
                {
                    dong.LabelDataJson.Add(a.Payload.DataJson);
                    coDong = coDong + a.Payload.DataJson.Length;
                }

                if (lo != null && coLo + coDong > CoLo)
                {
                    await GuiAsync(lo, kq, ct);
                    lo = null;
                }

                if (lo == null)
                {
                    lo = TaoLo(tapNhanJson, nguong, f1ToiThieu);
                    coLo = 0;
                }

                lo.Samples.Add(dong);
                coLo = coLo + coDong;
            }

            if (lo != null)
            {
                await GuiAsync(lo, kq, ct);
            }

            return kq;
        }

        private static AggregateRequest TaoLo(string tapNhanJson, NguongKhop nguong, double f1ToiThieu)
        {
            AggregateRequest req = new AggregateRequest { LabelSchemaJson = tapNhanJson, MinF1 = f1ToiThieu };
            foreach (KeyValuePair<string, double> n in nguong.TheoLoai)
            {
                req.Thresholds[n.Key] = n.Value;
            }

            return req;
        }

        private async Task GuiAsync(AggregateRequest req, Dictionary<Guid, JsonObject> kq, CancellationToken ct)
        {
            AggregateResponse res;
            try
            {
                res = await _client.AggregateSamplesAsync(
                    req, InternalGrpc.Khoa(_options), _clock.GetUtcNow().UtcDateTime.Add(HetGio), ct);
            }
            catch (RpcException ex) when (InternalGrpc.LaLoiTamThoi(ex))
            {
                throw new DichVuKhongSanSangException("quality-svc tam thoi khong gop duoc nhan. Thu lai sau.", ex);
            }

            foreach (SampleAggregate s in res.Samples)
            {
                JsonObject? tools = JsonNode.Parse(s.ToolsJson) as JsonObject;
                if (tools == null)
                {
                    throw new InvalidOperationException("quality-svc tra ket qua gop khong phai object cho mau " + s.SampleId + ".");
                }

                kq[Guid.Parse(s.SampleId)] = tools;
            }
        }
    }
}
