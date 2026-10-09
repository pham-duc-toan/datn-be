using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Annotation.Domain.Annotations;
using Crowd.Annotation.Infrastructure.Persistence;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.Contracts.Gate;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Annotation.Api.Consumers
{
    /// <summary>
    /// gate.solved (gate-svc): luu nhan cau THAT cua khach vang lai da qua cau vang, nguon
    /// linkGateway. Doanh nghiep duyet / loai nhu nhan thuong de chon loc du lieu; khong co
    /// tien theo nhan. Khong phat annotation.submitted: nhan cong link KHONG vao dong thuan /
    /// redundancy cua kenh chuyen nghiep.
    /// </summary>
    public sealed class GateSolvedProcessor : IEventProcessor<GateSolved>
    {
        private readonly AnnotationDbContext _db;

        public GateSolvedProcessor(AnnotationDbContext db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            _db = db;
        }

        public async Task XuLyAsync(EventEnvelope<GateSolved> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            GateSolved p = envelope.Payload;
            Dictionary<Guid, GateLabel> theoId = new Dictionary<Guid, GateLabel>();
            foreach (GateLabel l in p.Labels)
            {
                theoId[IdTatDinh(p.SessionId, l.SampleId)] = l;
            }

            // gate relay giao lai (at-least-once, eventId moi): id tat dinh chan nhan thu hai.
            List<Guid> ids = theoId.Keys.ToList();
            HashSet<Guid> daCo = (await _db.Annotations.Where(a => ids.Contains(a.AssignmentId)).Select(a => a.AssignmentId).ToListAsync(ct)).ToHashSet();

            foreach (KeyValuePair<Guid, GateLabel> cap in theoId)
            {
                if (daCo.Contains(cap.Key))
                {
                    continue;
                }

                GateLabel l = cap.Value;
                _db.Annotations.Add(LabelAnnotation.TaoTuCongLink(
                    cap.Key, p.ProjectId, l.SampleId, l.StorageKey, l.SampleContent, l.SampleMetadata, l.LabelPayload, p.SolvedAt));
            }
        }

        /// <summary>UUID tat dinh tu (phien, mau): cung dau vao → cung id, khac phien → khac id.</summary>
        public static Guid IdTatDinh(Guid sessionId, Guid sampleId)
        {
            byte[] dauVao = new byte[32];
            sessionId.TryWriteBytes(dauVao.AsSpan(0, 16));
            sampleId.TryWriteBytes(dauVao.AsSpan(16, 16));
            byte[] bam = SHA256.HashData(dauVao);
            byte[] g = new byte[16];
            Array.Copy(bam, g, 16);
            g[7] = (byte)((g[7] & 0x0F) | 0x50);
            g[8] = (byte)((g[8] & 0x3F) | 0x80);
            return new Guid(g);
        }
    }
}
