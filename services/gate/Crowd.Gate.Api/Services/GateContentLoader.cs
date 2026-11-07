using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Internal;
using Crowd.Contracts.Grpc.Project;
using Crowd.Gate.Domain;
using Crowd.Gate.Infrastructure.Persistence;
using Crowd.Labeling;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Crowd.Gate.Api.Services
{
    /// <summary>
    /// Nap mau + cau vang kiem tra cua du an vua publish bat cong link, qua gRPC tu project-svc (kien truc 3.23).
    ///
    /// Thay cho viec nghe dataset.ingested / gold_set.updated cua MOI du an: gate chi giu ban sao cua du an no
    /// THAT SU phuc vu. Viec nen, KHONG o duong nong — khach van doc tu GateCatalog (bo nho) nap tu ban sao.
    /// Consumer project.published khong goi gRPC (project-svc sap thi message khong roi vao DLQ): no chi tao dong
    /// gate_projects voi content_loaded_at NULL, worker (ContentLoadWorker) goi ham nay va thu lai vong sau.
    /// </summary>
    public sealed class GateContentLoader
    {
        /// <summary>So du an toi da moi vong — du an lon nap lau, khong don mot luc.</summary>
        private const int DuAnMoiVong = 10;

        private static readonly TimeSpan HetGioKeo = TimeSpan.FromMinutes(5);

        private readonly GateDbContext _db;
        private readonly GateData.GateDataClient _client;
        private readonly InternalApiOptions _internal;
        private readonly TimeProvider _clock;
        private readonly ILogger<GateContentLoader> _logger;

        public GateContentLoader(
            GateDbContext db,
            GateData.GateDataClient client,
            IOptions<InternalApiOptions> internalApi,
            TimeProvider clock,
            ILogger<GateContentLoader> logger)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            if (internalApi == null)
            {
                throw new ArgumentNullException(nameof(internalApi));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _db = db;
            _client = client;
            _internal = internalApi.Value;
            _clock = clock;
            _logger = logger;
        }

        /// <summary>Nap cac du an dang cho. Tra so du an vua nap xong. project-svc khong tra loi → RpcException.</summary>
        public async Task<int> NapCacDuAnChoAsync(CancellationToken ct)
        {
            List<Guid> cho = await _db.Projects.AsNoTracking()
                .Where(p => p.ContentLoadedAt == null && p.Status != TrangThaiDuAnCong.Closed)
                .OrderBy(p => p.UpdatedAt)
                .Select(p => p.ProjectId)
                .Take(DuAnMoiVong)
                .ToListAsync(ct);

            int xong = 0;
            foreach (Guid projectId in cho)
            {
                if (await NapAsync(projectId, ct))
                {
                    xong++;
                }
            }

            return xong;
        }

        private async Task<bool> NapAsync(Guid projectId, CancellationToken ct)
        {
            // 1. Keo HET qua mang truoc, ngoai transaction — khong giu khoa dong du an trong luc doi project-svc.
            List<MauCong> mau = new List<MauCong>();
            List<CauVangCong> vang = new List<CauVangCong>();
            Metadata khoa = InternalGrpc.Khoa(_internal);
            try
            {
                using (AsyncServerStreamingCall<GateContentItem> goi = _client.StreamGateContent(
                    new GateContentRequest { ProjectId = projectId.ToString() }, khoa, _clock.GetUtcNow().UtcDateTime.Add(HetGioKeo), ct))
                {
                    await foreach (GateContentItem x in goi.ResponseStream.ReadAllAsync(ct))
                    {
                        if (x.ItemCase == GateContentItem.ItemOneofCase.Sample)
                        {
                            GateSample s = x.Sample;
                            mau.Add(MauCong.Tao(
                                Guid.Parse(s.SampleId),
                                projectId,
                                s.Modality,
                                s.HasStorageKey ? s.StorageKey : null,
                                s.HasContentJson ? RawJson.Tu(s.ContentJson) : null,
                                RawJson.Tu(s.MetadataJson)));
                        }
                        else if (x.ItemCase == GateContentItem.ItemOneofCase.Gold)
                        {
                            GateGoldItem g = x.Gold;
                            vang.Add(CauVangCong.Tao(
                                Guid.Parse(g.SampleId), projectId, LabelPayload.TuLuuTru(g.TaskType, g.SchemaVersion, g.DataJson)));
                        }
                    }
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
            {
                // project.published da toi ma project-svc noi chua publish / khong bat cong: lech trang thai, khong
                // tu doan — de nguyen, vong sau thu lai, log de admin thay.
                _logger.LogWarning("Du an {ProjectId}: project-svc tra NOT_FOUND khi keo mau cong link ({Detail})", projectId, ex.Status.Detail);
                return false;
            }

            // 2. Ghi trong MOT transaction, khoa dong du an: consumer project.cancelled / completed cung cap nhat
            // dong nay roi xoa mau → hai ben xep hang, khong de lai mau cua du an da dong.
            using (IDbContextTransaction tx = await _db.Database.BeginTransactionAsync(ct))
            {
                DuAnCong? d = await _db.Projects
                    .FromSqlInterpolated($"SELECT * FROM gate_projects WHERE project_id = {projectId} FOR UPDATE")
                    .FirstOrDefaultAsync(ct);
                if (d == null || d.ContentLoadedAt != null || d.Status == TrangThaiDuAnCong.Closed)
                {
                    await tx.CommitAsync(ct);
                    return false;
                }

                // Lan nap truoc chet giua chung khong de lai gi (cung transaction), nhung xoa truoc cho chac.
                await _db.GoldItems.Where(g => g.ProjectId == projectId).ExecuteDeleteAsync(ct);
                await _db.Samples.Where(m => m.ProjectId == projectId).ExecuteDeleteAsync(ct);
                _db.Samples.AddRange(mau);
                _db.GoldItems.AddRange(vang);
                d.DaNapNoiDung(_clock.GetUtcNow());
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }

            _logger.LogInformation("Cong link: nap du an {ProjectId} — {SoMau} mau, {SoVang} cau vang", projectId, mau.Count, vang.Count);
            return true;
        }
    }
}
