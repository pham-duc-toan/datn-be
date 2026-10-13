using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.Contracts.Annotation;
using Crowd.Contracts.Identity;
using Crowd.Contracts.Ledger;
using Crowd.Contracts.Payment;
using Crowd.Contracts.Project;
using Crowd.Notification.Api.Entities;
using Crowd.Notification.Api.Persistence;
using Crowd.Notification.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Notification.Api.Consumers
{
    // Moi event → toi da MOT thong bao cho MOT nguoi. Giao trung → processed_events chan (guard).
    // Processor KHONG SaveChanges (IdempotencyGuard luu mot lan cung dau vet).

    /// <summary>Lop co so: giu ThongBaoWriter + DbContext, kiem envelope null.</summary>
    public abstract class ThongBaoProcessor<TPayload> : IEventProcessor<TPayload>
        where TPayload : class, IEventPayload
    {
        protected ThongBaoProcessor(ThongBaoWriter writer, NotificationDbContext db)
        {
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            Writer = writer;
            Db = db;
        }

        protected ThongBaoWriter Writer { get; }

        protected NotificationDbContext Db { get; }

        public Task XuLyAsync(EventEnvelope<TPayload> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            return XuLyPayloadAsync(envelope.Payload, ct);
        }

        protected abstract Task XuLyPayloadAsync(TPayload p, CancellationToken ct);

        protected async Task<Guid?> ChuDuAnAsync(Guid projectId, CancellationToken ct)
        {
            ChuDuAn? c = await Db.ChuDuAns.AsNoTracking().FirstOrDefaultAsync(x => x.ProjectId == projectId, ct);
            if (c == null)
            {
                return null;
            }

            return c.OwnerId;
        }
    }

    // ===================================================================== ban sao

    /// <summary>user.registered → dia chi lien lac (cap nhat neu giao lai).</summary>
    public sealed class UserRegisteredProcessor : ThongBaoProcessor<UserRegistered>
    {
        public UserRegisteredProcessor(ThongBaoWriter writer, NotificationDbContext db)
            : base(writer, db)
        {
        }

        protected override async Task XuLyPayloadAsync(UserRegistered p, CancellationToken ct)
        {
            LienHe? lh = await Db.LienHes.FirstOrDefaultAsync(x => x.UserId == p.UserId, ct);
            if (lh == null)
            {
                Db.LienHes.Add(LienHe.Tao(p.UserId, p.Email, p.DisplayName, p.Roles));
            }
            else
            {
                lh.DatVaiTro(p.Roles);
            }
        }
    }

    public sealed class PublishRequestedProcessor : ThongBaoProcessor<ProjectPublishRequested>
    {
        public PublishRequestedProcessor(ThongBaoWriter writer, NotificationDbContext db)
            : base(writer, db)
        {
        }

        protected override async Task XuLyPayloadAsync(ProjectPublishRequested p, CancellationToken ct)
        {
            if (!await Db.ChuDuAns.AnyAsync(x => x.ProjectId == p.ProjectId, ct))
            {
                Db.ChuDuAns.Add(ChuDuAn.Tao(p.ProjectId, p.OwnerId));
            }
        }
    }

    public sealed class PayoutRequestedProcessor : ThongBaoProcessor<PayoutRequested>
    {
        public PayoutRequestedProcessor(ThongBaoWriter writer, NotificationDbContext db)
            : base(writer, db)
        {
        }

        protected override async Task XuLyPayloadAsync(PayoutRequested p, CancellationToken ct)
        {
            if (!await Db.ChuLenhRuts.AnyAsync(x => x.WithdrawalId == p.WithdrawalId, ct))
            {
                Db.ChuLenhRuts.Add(ChuLenhRut.Tao(p.WithdrawalId, p.LabelerId, p.NetAmountVnd));
            }
        }
    }

    // ===================================================================== tai khoan, KYB

    public sealed class BusinessVerifiedProcessor : ThongBaoProcessor<BusinessVerified>
    {
        public BusinessVerifiedProcessor(ThongBaoWriter writer, NotificationDbContext db)
            : base(writer, db)
        {
        }

        protected override Task XuLyPayloadAsync(BusinessVerified p, CancellationToken ct)
        {
            return Writer.GhiAsync(p.BusinessId, LoaiThongBao.KetQuaKyb, NoiDungThongBao.DoanhNghiepDaXacMinh(), ct);
        }
    }

    public sealed class BusinessKybRejectedProcessor : ThongBaoProcessor<BusinessKybRejected>
    {
        public BusinessKybRejectedProcessor(ThongBaoWriter writer, NotificationDbContext db)
            : base(writer, db)
        {
        }

        protected override Task XuLyPayloadAsync(BusinessKybRejected p, CancellationToken ct)
        {
            return Writer.GhiAsync(p.BusinessId, LoaiThongBao.KetQuaKyb, NoiDungThongBao.DoanhNghiepBiTuChoi(p.Reason), ct);
        }
    }

    public sealed class UserBlockedProcessor : ThongBaoProcessor<UserBlocked>
    {
        public UserBlockedProcessor(ThongBaoWriter writer, NotificationDbContext db)
            : base(writer, db)
        {
        }

        protected override Task XuLyPayloadAsync(UserBlocked p, CancellationToken ct)
        {
            return Writer.GhiAsync(p.UserId, LoaiThongBao.TaiKhoan, NoiDungThongBao.TaiKhoanBiKhoa(p.Reason), ct);
        }
    }

    public sealed class UserUnblockedProcessor : ThongBaoProcessor<UserUnblocked>
    {
        public UserUnblockedProcessor(ThongBaoWriter writer, NotificationDbContext db)
            : base(writer, db)
        {
        }

        protected override Task XuLyPayloadAsync(UserUnblocked p, CancellationToken ct)
        {
            return Writer.GhiAsync(p.UserId, LoaiThongBao.TaiKhoan, NoiDungThongBao.TaiKhoanMoKhoa(), ct);
        }
    }

    // ===================================================================== doanh nghiep: tien, du an

    public sealed class DepositConfirmedProcessor : ThongBaoProcessor<DepositConfirmed>
    {
        public DepositConfirmedProcessor(ThongBaoWriter writer, NotificationDbContext db)
            : base(writer, db)
        {
        }

        protected override Task XuLyPayloadAsync(DepositConfirmed p, CancellationToken ct)
        {
            return Writer.GhiAsync(p.BusinessId, LoaiThongBao.NapTien, NoiDungThongBao.NapTienThanhCong(p.AmountVnd), ct);
        }
    }

    public sealed class ProjectPublishedProcessor : ThongBaoProcessor<ProjectPublished>
    {
        public ProjectPublishedProcessor(ThongBaoWriter writer, NotificationDbContext db)
            : base(writer, db)
        {
        }

        protected override Task XuLyPayloadAsync(ProjectPublished p, CancellationToken ct)
        {
            return Writer.GhiAsync(p.OwnerId, LoaiThongBao.DuAn, NoiDungThongBao.DuAnDangChay(p.ProjectId), ct);
        }
    }

    public sealed class EscrowRejectedProcessor : ThongBaoProcessor<EscrowRejected>
    {
        public EscrowRejectedProcessor(ThongBaoWriter writer, NotificationDbContext db)
            : base(writer, db)
        {
        }

        protected override async Task XuLyPayloadAsync(EscrowRejected p, CancellationToken ct)
        {
            // escrow.rejected chi mang ProjectId; chu du an tu project.publish_requested (di TRUOC no).
            // Chua co (queue khac cham) → nem loi de broker giao lai sau.
            Guid? chu = await ChuDuAnAsync(p.ProjectId, ct);
            if (!chu.HasValue)
            {
                throw new InvalidOperationException("Chua biet chu du an " + p.ProjectId + " — cho project.publish_requested.");
            }

            await Writer.GhiAsync(chu.Value, LoaiThongBao.DuAn, NoiDungThongBao.DuAnThieuKyQuy(p.ProjectId, p.Reason), ct);
        }
    }

    public sealed class ProjectCancelledProcessor : ThongBaoProcessor<ProjectCancelled>
    {
        public ProjectCancelledProcessor(ThongBaoWriter writer, NotificationDbContext db)
            : base(writer, db)
        {
        }

        protected override Task XuLyPayloadAsync(ProjectCancelled p, CancellationToken ct)
        {
            // Huy luc con Nhap: chinh doanh nghiep vua bam — khong can bao.
            if (!p.WasEscrowed)
            {
                return Task.CompletedTask;
            }

            return Writer.GhiAsync(p.OwnerId, LoaiThongBao.DuAn, NoiDungThongBao.DuAnBiHuy(p.ProjectId, p.Reason), ct);
        }
    }

    public sealed class EscrowReleasedProcessor : ThongBaoProcessor<EscrowReleased>
    {
        public EscrowReleasedProcessor(ThongBaoWriter writer, NotificationDbContext db)
            : base(writer, db)
        {
        }

        protected override Task XuLyPayloadAsync(EscrowReleased p, CancellationToken ct)
        {
            return p.AmountVnd <= 0
                ? Task.CompletedTask
                : Writer.GhiAsync(p.OwnerId, LoaiThongBao.DuAn, NoiDungThongBao.HoanKyQuy(p.ProjectId, p.AmountVnd), ct);
        }
    }

    public sealed class RefundIssuedProcessor : ThongBaoProcessor<RefundIssued>
    {
        public RefundIssuedProcessor(ThongBaoWriter writer, NotificationDbContext db)
            : base(writer, db)
        {
        }

        protected override Task XuLyPayloadAsync(RefundIssued p, CancellationToken ct)
        {
            return p.AmountVnd <= 0
                ? Task.CompletedTask
                : Writer.GhiAsync(p.OwnerId, LoaiThongBao.DuAn, NoiDungThongBao.HoanKyQuy(p.ProjectId, p.AmountVnd), ct);
        }
    }

    // ===================================================================== labeler: nhan, tien

    public sealed class AnnotationApprovedProcessor : ThongBaoProcessor<AnnotationApproved>
    {
        public AnnotationApprovedProcessor(ThongBaoWriter writer, NotificationDbContext db)
            : base(writer, db)
        {
        }

        protected override Task XuLyPayloadAsync(AnnotationApproved p, CancellationToken ct)
        {
            // Nhan cong link: khach vang lai, khong co tai khoan de bao.
            if (!p.LabelerId.HasValue || p.Source != AnnotationSource.Professional)
            {
                return Task.CompletedTask;
            }

            Guid projectId = p.ProjectId;
            return Writer.GhiGopAsync(p.LabelerId.Value, LoaiThongBao.NhanDuyet, p.AmountVnd,
                (tong, soLan) => NoiDungThongBao.NhanDuocDuyet(projectId, tong, soLan), ct);
        }
    }

    public sealed class AnnotationRejectedProcessor : ThongBaoProcessor<AnnotationRejected>
    {
        public AnnotationRejectedProcessor(ThongBaoWriter writer, NotificationDbContext db)
            : base(writer, db)
        {
        }

        protected override Task XuLyPayloadAsync(AnnotationRejected p, CancellationToken ct)
        {
            if (!p.LabelerId.HasValue)
            {
                return Task.CompletedTask;
            }

            return Writer.GhiAsync(p.LabelerId.Value, LoaiThongBao.NhanTuChoi, NoiDungThongBao.NhanBiTuChoi(p.ProjectId, p.Reason, p.IsFinal), ct);
        }
    }

    public sealed class HoldExpiredProcessor : ThongBaoProcessor<HoldExpired>
    {
        public HoldExpiredProcessor(ThongBaoWriter writer, NotificationDbContext db)
            : base(writer, db)
        {
        }

        protected override Task XuLyPayloadAsync(HoldExpired p, CancellationToken ct)
        {
            return Writer.GhiGopAsync(p.LabelerId, LoaiThongBao.TienKhaDung, p.AmountVnd,
                (tong, soLan) => NoiDungThongBao.TienKhaDung(tong, soLan), ct);
        }
    }

    public sealed class PayoutCompletedProcessor : ThongBaoProcessor<PayoutCompleted>
    {
        public PayoutCompletedProcessor(ThongBaoWriter writer, NotificationDbContext db)
            : base(writer, db)
        {
        }

        protected override async Task XuLyPayloadAsync(PayoutCompleted p, CancellationToken ct)
        {
            ChuLenhRut c = await LenhRutAsync(Db, p.WithdrawalId, ct);
            await Writer.GhiAsync(c.LabelerId, LoaiThongBao.RutTien, NoiDungThongBao.RutThanhCong(c.NetAmountVnd), ct);
        }

        internal static async Task<ChuLenhRut> LenhRutAsync(NotificationDbContext db, Guid withdrawalId, CancellationToken ct)
        {
            // payout.requested (ledger → payment) luon di TRUOC; chua toi → giao lai sau.
            ChuLenhRut? c = await db.ChuLenhRuts.AsNoTracking().FirstOrDefaultAsync(x => x.WithdrawalId == withdrawalId, ct);
            if (c == null)
            {
                throw new InvalidOperationException("Chua biet chu lenh rut " + withdrawalId + " — cho payout.requested.");
            }

            return c;
        }
    }

    public sealed class PayoutFailedProcessor : ThongBaoProcessor<PayoutFailed>
    {
        public PayoutFailedProcessor(ThongBaoWriter writer, NotificationDbContext db)
            : base(writer, db)
        {
        }

        protected override async Task XuLyPayloadAsync(PayoutFailed p, CancellationToken ct)
        {
            ChuLenhRut c = await PayoutCompletedProcessor.LenhRutAsync(Db, p.WithdrawalId, ct);
            await Writer.GhiAsync(c.LabelerId, LoaiThongBao.RutTien, NoiDungThongBao.RutThatBai(p.Reason), ct);
        }
    }

    public sealed class WithdrawalRejectedProcessor : ThongBaoProcessor<WithdrawalRejected>
    {
        public WithdrawalRejectedProcessor(ThongBaoWriter writer, NotificationDbContext db)
            : base(writer, db)
        {
        }

        protected override Task XuLyPayloadAsync(WithdrawalRejected p, CancellationToken ct)
        {
            return Writer.GhiAsync(p.LabelerId, LoaiThongBao.RutTien, NoiDungThongBao.RutBiTuChoi(p.AmountVnd, p.Reason), ct);
        }
    }
}
