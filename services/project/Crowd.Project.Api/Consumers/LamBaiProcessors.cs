using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.Contracts.Gate;
using Crowd.Contracts.Identity;
using Crowd.Contracts.Quality;
using Crowd.Project.Api.Services;
using Crowd.Project.Domain.Annotations;
using Crowd.Project.Domain.Collections;
using Crowd.Project.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crowd.Project.Api.Consumers
{
    // Event tu SERVICE KHAC ma phan lam bai / nhan can (truoc day task-svc va annotation-svc nghe). Event cua
    // chinh project-svc (published, paused, member.*, dataset.ingested, gold_set.updated...) KHONG con nghe nua:
    // cung database nen goi thang trong cung transaction.
    //
    // GIAO KEO: KHONG SaveChanges — IdempotencyGuard luu + ghi processed_events + commit MOT lan.

    /// <summary>
    /// project.graded (quality-svc): ghi ket qua tung bai, chot nhan (dat → Accepted, truot → Excluded), mo han
    /// khieu nai; han 0 thi chot luon (du an Hoan thanh, phat project.completed).
    /// </summary>
    public sealed class ProjectGradedProcessor : IEventProcessor<ProjectGraded>
    {
        private readonly ProjectDbContext _db;
        private readonly ThuBaiService _thuBai;
        private readonly TimeProvider _clock;

        public ProjectGradedProcessor(ProjectDbContext db, ThuBaiService thuBai, TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (thuBai == null)
            {
                throw new ArgumentNullException(nameof(thuBai));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _db = db;
            _thuBai = thuBai;
            _clock = clock;
        }

        public async Task XuLyAsync(EventEnvelope<ProjectGraded> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            CollectionRound? dot = await LamBaiQueries.KhoaDotAsync(_db, envelope.Payload.ProjectId, ct);
            if (dot == null)
            {
                // Du an chua tung chay ma da cham la sai bat bien — nem loi de vao DLQ cho nguoi xem.
                throw new InvalidOperationException("project.graded cho du an chua co dot thu bai " + envelope.Payload.ProjectId + ".");
            }

            await _thuBai.GhiKetQuaChamAsync(dot, envelope.Payload, _clock.GetUtcNow(), Caller.HeThong(envelope.CorrelationId, envelope.EventId), ct);
        }
    }

    // ---------------------------------------------------------------------
    // user.blocked ← identity: huy bai dang lam
    // ---------------------------------------------------------------------

    /// <summary>
    /// user.blocked (identity): huy cac bai DANG LAM cua nguoi bi khoa. Chan MO bai moi khong can ban sao — luc
    /// mo bai project-svc hoi identity qua gRPC (kien truc 3.24). Giao lai: bai da huy thi khong con gi de huy.
    /// </summary>
    public sealed class UserBlockedProcessor : IEventProcessor<UserBlocked>
    {
        private readonly BaiLamRevoker _revoker;
        private readonly TimeProvider _clock;

        public UserBlockedProcessor(BaiLamRevoker revoker, TimeProvider clock)
        {
            if (revoker == null)
            {
                throw new ArgumentNullException(nameof(revoker));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _revoker = revoker;
            _clock = clock;
        }

        public async Task XuLyAsync(EventEnvelope<UserBlocked> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            await _revoker.ThuHoiAsync(null, envelope.Payload.UserId, _clock.GetUtcNow(), ct);
        }
    }

    // ---------------------------------------------------------------------
    // gate.solved ← gate-svc: nhan cua khach vang lai qua trang vuot link
    // ---------------------------------------------------------------------

    /// <summary>
    /// gate.solved: khach da qua cau vang → moi cau tra loi that thanh MOT nhan Accepted (source LinkGateway),
    /// khong thuoc bai lam nao. Id tat dinh (phien, mau) — event giao lai khong tao nhan thu hai.
    /// </summary>
    public sealed class GateSolvedProcessor : IEventProcessor<GateSolved>
    {
        private readonly ProjectDbContext _db;

        public GateSolvedProcessor(ProjectDbContext db)
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
            HashSet<Guid> daCo = (await _db.Annotations
                .Where(a => a.ProjectId == p.ProjectId && ids.Contains(a.AttemptId))
                .Select(a => a.AttemptId)
                .ToListAsync(ct)).ToHashSet();

            foreach (KeyValuePair<Guid, GateLabel> cap in theoId)
            {
                if (daCo.Contains(cap.Key))
                {
                    continue;
                }

                GateLabel l = cap.Value;
                _db.Annotations.Add(LabelAnnotation.TaoTuCongLink(cap.Key, p.ProjectId, l.SampleId, l.LabelPayload));
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
