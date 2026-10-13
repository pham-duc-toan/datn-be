using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Contracts.Admin;
using Crowd.Contracts.Identity;
using Crowd.Contracts.Ledger;
using Crowd.Notification.Api.Persistence;
using Crowd.Notification.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Notification.Api.Consumers
{
    // Email giao dich (FC-01, FC-03), tranh chap (FM-06), cap do / huy hieu (FL-12),
    // thong bao toan he thong (FM-09). Processor KHONG SaveChanges.

    /// <summary>user.email_verification_requested → email giao dich chua link xac thuc.</summary>
    public sealed class EmailVerificationRequestedProcessor : ThongBaoProcessor<EmailVerificationRequested>
    {
        public EmailVerificationRequestedProcessor(ThongBaoWriter writer, NotificationDbContext db)
            : base(writer, db)
        {
        }

        protected override Task XuLyPayloadAsync(EmailVerificationRequested p, CancellationToken ct)
        {
            Writer.GhiEmailGiaoDich(p.UserId, p.Email, p.DisplayName, "Xác thực email Crowd Label",
                NoiDungThongBao.EmailXacThuc(p.DisplayName, p.VerifyUrl, p.ExpiresAt));
            return Task.CompletedTask;
        }
    }

    /// <summary>user.password_reset_requested → email giao dich chua link dat lai mat khau.</summary>
    public sealed class PasswordResetRequestedProcessor : ThongBaoProcessor<PasswordResetRequested>
    {
        public PasswordResetRequestedProcessor(ThongBaoWriter writer, NotificationDbContext db)
            : base(writer, db)
        {
        }

        protected override Task XuLyPayloadAsync(PasswordResetRequested p, CancellationToken ct)
        {
            Writer.GhiEmailGiaoDich(p.UserId, p.Email, p.DisplayName, "Đặt lại mật khẩu Crowd Label",
                NoiDungThongBao.EmailDatLaiMatKhau(p.DisplayName, p.ResetUrl, p.ExpiresAt));
            return Task.CompletedTask;
        }
    }

    /// <summary>dispute.opened → bao labeler (khoan treo bi giu). Doanh nghiep tu mo nen khong bao lai.</summary>
    public sealed class DisputeOpenedProcessor : ThongBaoProcessor<DisputeOpened>
    {
        public DisputeOpenedProcessor(ThongBaoWriter writer, NotificationDbContext db)
            : base(writer, db)
        {
        }

        protected override Task XuLyPayloadAsync(DisputeOpened p, CancellationToken ct)
        {
            return Writer.GhiAsync(p.LabelerId, LoaiThongBao.TranhChap, NoiDungThongBao.TranhChapMo(p.ProjectId, p.AmountVnd, p.Reason), ct);
        }
    }

    /// <summary>dispute.resolved → bao CA HAI ben ket cuc (email bat buoc: lien quan tien).</summary>
    public sealed class DisputeResolvedProcessor : ThongBaoProcessor<DisputeResolved>
    {
        public DisputeResolvedProcessor(ThongBaoWriter writer, NotificationDbContext db)
            : base(writer, db)
        {
        }

        protected override async Task XuLyPayloadAsync(DisputeResolved p, CancellationToken ct)
        {
            bool doanhNghiepThang = p.Outcome == DisputeOutcome.RefundBusiness;
            await Writer.GhiAsync(p.LabelerId, LoaiThongBao.TranhChap,
                NoiDungThongBao.TranhChapKetQua(p.ProjectId, p.AmountVnd, true, doanhNghiepThang, p.Note), ct);
            await Writer.GhiAsync(p.BusinessId, LoaiThongBao.TranhChap,
                NoiDungThongBao.TranhChapKetQua(p.ProjectId, p.AmountVnd, false, doanhNghiepThang, p.Note), ct);
        }
    }

    /// <summary>level.changed (identity) → bao cap moi.</summary>
    public sealed class LevelChangedProcessor : ThongBaoProcessor<LevelChanged>
    {
        public LevelChangedProcessor(ThongBaoWriter writer, NotificationDbContext db)
            : base(writer, db)
        {
        }

        protected override Task XuLyPayloadAsync(LevelChanged p, CancellationToken ct)
        {
            return Writer.GhiAsync(p.UserId, LoaiThongBao.CapDo, NoiDungThongBao.CapDoMoi(p.Level), ct);
        }
    }

    /// <summary>badge.awarded (identity) → bao huy hieu moi.</summary>
    public sealed class BadgeAwardedProcessor : ThongBaoProcessor<BadgeAwarded>
    {
        public BadgeAwardedProcessor(ThongBaoWriter writer, NotificationDbContext db)
            : base(writer, db)
        {
        }

        protected override Task XuLyPayloadAsync(BadgeAwarded p, CancellationToken ct)
        {
            return Writer.GhiAsync(p.UserId, LoaiThongBao.CapDo, NoiDungThongBao.HuyHieuMoi(p.Name), ct);
        }
    }

    /// <summary>
    /// system.announcement (admin) → mot thong bao cho moi nguoi dung thuoc doi tuong (theo vai tro trong
    /// ban sao lien he). Email chi khi admin chon gui email va tuy chon cua tung nguoi cho phep.
    /// </summary>
    public sealed class SystemAnnouncementProcessor : ThongBaoProcessor<SystemAnnouncement>
    {
        public SystemAnnouncementProcessor(ThongBaoWriter writer, NotificationDbContext db)
            : base(writer, db)
        {
        }

        protected override async Task XuLyPayloadAsync(SystemAnnouncement p, CancellationToken ct)
        {
            List<Guid> nguoiNhan;
            if (p.Audience == "all")
            {
                nguoiNhan = await Db.LienHes.AsNoTracking().Select(l => l.UserId).ToListAsync(ct);
            }
            else
            {
                string vaiTro = p.Audience;
                nguoiNhan = await Db.LienHes.AsNoTracking()
                    .Where(l => EF.Property<List<string>>(l, "_roles").Contains(vaiTro))
                    .Select(l => l.UserId)
                    .ToListAsync(ct);
            }

            await Writer.GhiHangLoatAsync(nguoiNhan, LoaiThongBao.HeThong, NoiDungThongBao.ThongBaoHeThong(p.Title, p.Body), p.SendEmail, ct);
        }
    }
}
