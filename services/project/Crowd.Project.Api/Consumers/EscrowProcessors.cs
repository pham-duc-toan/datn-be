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

            // Kien truc 3.29. Du an da huy / da xoa ma ledger van giu ky quy: tien co the dang ket → LOAI 2 (admin
            // kiem tra va hoan tay). Du an da qua buoc cho ky quy (duyet roi, dang chay...) → LOAI 1, bo qua.
            if (duAn == null || duAn.Status == ProjectStatus.Cancelled)
            {
                string ten = duAn == null ? "(dự án đã bị xoá)" : "“" + duAn.Name + "”";
                throw new CanXuLyTayException(new ViecCanXuLyTay
                {
                    Loai = "ky_quy_du_an_da_huy",
                    TieuDe = "Kiểm tra tiền ký quỹ của dự án đã huỷ",
                    MoTa = "Hệ thống đã giữ " + ViecCanXuLyTay.Tien(envelope.Payload.AmountVnd) + " tiền ký quỹ cho dự án " + ten
                        + " nhưng dự án đã bị huỷ hoặc xoá trước khi ký quỹ xong. Mở ví của doanh nghiệp: nếu số tiền này chưa được hoàn thì hoàn lại cho doanh nghiệp.",
                    ProjectId = duAn == null ? null : duAn.Id,
                    UserId = duAn == null ? null : duAn.OwnerId,
                    AmountVnd = envelope.Payload.AmountVnd,
                });
            }

            if (duAn.Status != ProjectStatus.PendingEscrow)
            {
                throw new BoQuaEventException("du an " + duAn.Id + " da o trang thai " + duAn.Status + ", khong con cho ky quy");
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

            // Ky quy bi tu choi = ledger KHONG giu tien → du an da doi trang thai / da xoa thi khong con gi de lam (loai 1).
            if (duAn == null || duAn.Status != ProjectStatus.PendingEscrow)
            {
                throw new BoQuaEventException("du an " + envelope.Payload.ProjectId + " khong con cho ky quy ("
                    + (duAn == null ? "da xoa" : duAn.Status.ToString()) + ")");
            }

            duAn.KyQuyBiTuChoi(envelope.Payload.Reason, _clock.GetUtcNow());

            _logger.LogInformation("Du an {ProjectId} bi tu choi ky quy: {Reason}", duAn.Id, envelope.Payload.Reason);
        }
    }
}
