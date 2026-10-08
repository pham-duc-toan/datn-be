using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.Contracts.Ledger;
using Crowd.Project.Api.Services;
using Crowd.Project.Domain.Projects;
using Crowd.Project.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crowd.Project.Api.Consumers
{
    /// <summary>
    /// SAGA BUOC 2 — nhanh thanh cong: ledger da giu tien.
    /// Cho ky quy → Cho duyet.
    ///
    /// KHONG SaveChanges: EventConsumer boc processor trong IdempotencyGuard,
    /// guard luu thay doi + dau vet processed_events trong mot transaction.
    /// </summary>
    public sealed class EscrowReservedProcessor : IEventProcessor<EscrowReserved>
    {
        private readonly ProjectDbContext _db;
        private readonly TimeProvider _clock;
        private readonly ILogger<EscrowReservedProcessor> _logger;
        private readonly ProjectService _projects;

        public EscrowReservedProcessor(
            ProjectDbContext db, TimeProvider clock, ILogger<EscrowReservedProcessor> logger, ProjectService projects)
        {
            if (projects == null)
            {
                throw new ArgumentNullException(nameof(projects));
            }

            _projects = projects;

            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
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
            _clock = clock;
            _logger = logger;
        }

        public async Task XuLyAsync(EventEnvelope<EscrowReserved> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            LabelingProject? duAn = await _db.Projects.FirstOrDefaultAsync(p => p.Id == envelope.Payload.ProjectId, ct);

            // Event ve mot du an khong con o trang thai cho: KHONG nem loi. Nem
            // loi thi message quay vong 5 lan roi vao DLQ ma khong sua duoc gi.
            // Ghi log canh bao la du — day la tinh huong can nguoi xem (vd ledger
            // giu tien cho du an da bi huy → can hoan tay).
            if (duAn == null || duAn.Status != ProjectStatus.PendingEscrow)
            {
                _logger.LogWarning(
                    "escrow.reserved {EventId} cho du an {ProjectId} khong o trang thai PendingEscrow ({Status}) — bo qua",
                    envelope.EventId,
                    envelope.Payload.ProjectId,
                    duAn == null ? "khong ton tai" : duAn.Status.ToString());
                return;
            }

            duAn.XacNhanDaKyQuy(_clock.GetUtcNow());

            // Setting project.auto_approve: duyet ngay, khong cho admin.
            await _projects.TuDuyetNeuBatAsync(duAn, Caller.HeThong(envelope.CorrelationId, envelope.EventId), ct);

            _logger.LogInformation(
                "Du an {ProjectId} da ky quy {AmountVnd}d, chuyen sang cho duyet",
                duAn.Id,
                envelope.Payload.AmountVnd);
        }
    }

    /// <summary>SAGA BUOC 2 — nhanh that bai: khong du tien. Ve Nhap kem ly do.</summary>
    public sealed class EscrowRejectedProcessor : IEventProcessor<EscrowRejected>
    {
        private readonly ProjectDbContext _db;
        private readonly TimeProvider _clock;
        private readonly ILogger<EscrowRejectedProcessor> _logger;

        public EscrowRejectedProcessor(ProjectDbContext db, TimeProvider clock, ILogger<EscrowRejectedProcessor> logger)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
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
            _clock = clock;
            _logger = logger;
        }

        public async Task XuLyAsync(EventEnvelope<EscrowRejected> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            LabelingProject? duAn = await _db.Projects.FirstOrDefaultAsync(p => p.Id == envelope.Payload.ProjectId, ct);

            if (duAn == null || duAn.Status != ProjectStatus.PendingEscrow)
            {
                _logger.LogWarning(
                    "escrow.rejected {EventId} cho du an {ProjectId} khong o trang thai PendingEscrow — bo qua",
                    envelope.EventId,
                    envelope.Payload.ProjectId);
                return;
            }

            duAn.KyQuyBiTuChoi(envelope.Payload.Reason, _clock.GetUtcNow());

            _logger.LogInformation("Du an {ProjectId} bi tu choi ky quy: {Reason}", duAn.Id, envelope.Payload.Reason);
        }
    }
}
