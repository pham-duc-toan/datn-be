using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.Contracts.Identity;
using Crowd.Contracts.Ledger;
using Crowd.Contracts.Payment;
using Crowd.Contracts.Project;
using Crowd.Contracts.Quality;
using Crowd.Contracts.Tasking;
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

        protected override Task XuLyPayloadAsync(EscrowRejected p, CancellationToken ct)
        {
            return Writer.GhiAsync(p.OwnerId, LoaiThongBao.DuAn, NoiDungThongBao.DuAnThieuKyQuy(p.ProjectId, p.Reason), ct);
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

    // ===================================================================== labeler: ket qua bai, tien

    /// <summary>
    /// project.graded: MOI labeler mot thong bao dat / chua dat, chu du an mot thong bao tong ket. Tien thuong
    /// bao rieng qua hold.expired (ledger.reward_hold = 0 → giai phong ngay).
    /// </summary>
    public sealed class ProjectGradedProcessor : ThongBaoProcessor<ProjectGraded>
    {
        public ProjectGradedProcessor(ThongBaoWriter writer, NotificationDbContext db)
            : base(writer, db)
        {
        }

        protected override async Task XuLyPayloadAsync(ProjectGraded p, CancellationToken ct)
        {
            foreach (GradedAttempt a in p.Attempts)
            {
                NoiDung n;
                if (a.Passed)
                {
                    n = NoiDungThongBao.BaiDat(a.AttemptId, a.AgreementPercent, p.PassThresholdPercent, p.MajorityUsed, a.Rewarded);
                }
                else if (a.Rewarded)
                {
                    n = NoiDungThongBao.BaiLapSuat(a.AttemptId, a.AgreementPercent, p.PassThresholdPercent);
                }
                else
                {
                    n = NoiDungThongBao.BaiTruot(a.AttemptId, a.AgreementPercent, p.PassThresholdPercent, a.GoldCorrect, a.GoldTotal, p.MajorityUsed);
                }

                await Writer.GhiAsync(a.LabelerId, LoaiThongBao.KetQuaBai, n, ct);
            }

            int dat = 0;
            int thuong = 0;
            foreach (GradedAttempt a in p.Attempts)
            {
                if (a.Passed)
                {
                    dat++;
                }

                if (a.Rewarded)
                {
                    thuong++;
                }
            }

            await Writer.GhiAsync(p.OwnerId, LoaiThongBao.DuAn, NoiDungThongBao.DuAnDaCham(p.ProjectId, dat, thuong, p.Attempts.Count), ct);
        }
    }

    public sealed class AttemptAppealResolvedProcessor : ThongBaoProcessor<AttemptAppealResolved>
    {
        public AttemptAppealResolvedProcessor(ThongBaoWriter writer, NotificationDbContext db)
            : base(writer, db)
        {
        }

        protected override Task XuLyPayloadAsync(AttemptAppealResolved p, CancellationToken ct)
        {
            return Writer.GhiAsync(p.LabelerId, LoaiThongBao.KetQuaBai, NoiDungThongBao.KetQuaKhieuNai(p.AttemptId, p.Accepted, p.Rewarded, p.Note), ct);
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

        protected override Task XuLyPayloadAsync(PayoutCompleted p, CancellationToken ct)
        {
            return Writer.GhiAsync(p.LabelerId, LoaiThongBao.RutTien, NoiDungThongBao.RutThanhCong(p.NetAmountVnd), ct);
        }
    }

    public sealed class PayoutFailedProcessor : ThongBaoProcessor<PayoutFailed>
    {
        public PayoutFailedProcessor(ThongBaoWriter writer, NotificationDbContext db)
            : base(writer, db)
        {
        }

        protected override Task XuLyPayloadAsync(PayoutFailed p, CancellationToken ct)
        {
            return Writer.GhiAsync(p.LabelerId, LoaiThongBao.RutTien, NoiDungThongBao.RutThatBai(p.Reason), ct);
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
