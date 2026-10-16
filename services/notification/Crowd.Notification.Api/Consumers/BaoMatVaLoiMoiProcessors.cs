using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Settings;
using Crowd.Contracts.Admin;
using Crowd.Contracts.Identity;
using Crowd.Contracts.Project;
using Crowd.Notification.Api.Persistence;
using Crowd.Notification.Api.Services;

namespace Crowd.Notification.Api.Consumers
{
    // Bao mat tai khoan (2FA, xac minh danh tinh), loi moi vao du an, canh bao van hanh.
    // Processor KHONG SaveChanges.

    /// <summary>user.two_factor_changed → thong bao + email BAT BUOC (loai BaoMat).</summary>
    public sealed class TwoFactorChangedProcessor : ThongBaoProcessor<UserTwoFactorChanged>
    {
        public TwoFactorChangedProcessor(ThongBaoWriter writer, NotificationDbContext db)
            : base(writer, db)
        {
        }

        protected override Task XuLyPayloadAsync(UserTwoFactorChanged p, CancellationToken ct)
        {
            return Writer.GhiAsync(p.UserId, LoaiThongBao.BaoMat, NoiDungThongBao.HaiLopThayDoi(p.Enabled, p.Reason), ct);
        }
    }

    /// <summary>user.identity_verification_submitted → xac nhan da nhan ho so (nguoi nop).</summary>
    public sealed class IdentityVerificationSubmittedProcessor : ThongBaoProcessor<IdentityVerificationSubmitted>
    {
        public IdentityVerificationSubmittedProcessor(ThongBaoWriter writer, NotificationDbContext db)
            : base(writer, db)
        {
        }

        protected override Task XuLyPayloadAsync(IdentityVerificationSubmitted p, CancellationToken ct)
        {
            return Writer.GhiAsync(p.UserId, LoaiThongBao.XacMinhDanhTinh, NoiDungThongBao.XacMinhDanhTinhDaNop(), ct);
        }
    }

    public sealed class IdentityVerifiedProcessor : ThongBaoProcessor<UserIdentityVerified>
    {
        public IdentityVerifiedProcessor(ThongBaoWriter writer, NotificationDbContext db)
            : base(writer, db)
        {
        }

        protected override Task XuLyPayloadAsync(UserIdentityVerified p, CancellationToken ct)
        {
            return Writer.GhiAsync(p.UserId, LoaiThongBao.XacMinhDanhTinh, NoiDungThongBao.XacMinhDanhTinhDuyet(), ct);
        }
    }

    public sealed class IdentityRejectedProcessor : ThongBaoProcessor<UserIdentityRejected>
    {
        public IdentityRejectedProcessor(ThongBaoWriter writer, NotificationDbContext db)
            : base(writer, db)
        {
        }

        protected override Task XuLyPayloadAsync(UserIdentityRejected p, CancellationToken ct)
        {
            return Writer.GhiAsync(p.UserId, LoaiThongBao.XacMinhDanhTinh, NoiDungThongBao.XacMinhDanhTinhTuChoi(p.Reason), ct);
        }
    }

    /// <summary>
    /// project.invitation_created → nguoi da co tai khoan: thong bao (+ email theo tuy chon). Email chua co tai
    /// khoan: email giao dich kem link dang ky — dang ky dung email do thi project-svc tu gan loi moi
    /// (user.email_verified).
    /// </summary>
    public sealed class InvitationCreatedProcessor : ThongBaoProcessor<ProjectInvitationCreated>
    {
        private readonly ISettings _settings;

        public InvitationCreatedProcessor(ThongBaoWriter writer, NotificationDbContext db, ISettings settings)
            : base(writer, db)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            _settings = settings;
        }

        protected override Task XuLyPayloadAsync(ProjectInvitationCreated p, CancellationToken ct)
        {
            if (p.UserId.HasValue)
            {
                return Writer.GhiAsync(p.UserId.Value, LoaiThongBao.LoiMoi,
                    NoiDungThongBao.LoiMoiDuAn(p.ProjectId, p.ProjectName, p.RequiresConsent, p.ExpiresAt), ct);
            }

            string link = _settings.Chuoi(SettingKeys.IdentityFrontendUrl).TrimEnd('/')
                + "/dang-ky?email=" + Uri.EscapeDataString(p.Email) + "&invitation=" + p.InvitationId.ToString("D");
            return Writer.GhiEmailGiaoDichAsync(null, p.Email, p.Email, NoiDungThongBao.EmailLoiMoiNguoiMoi(p.ProjectName, link, p.ExpiresAt), ct);
        }
    }

    /// <summary>ops.dlq_alert (admin-svc) → email toi ops.alert_email (trong = khong gui, admin xem /admin/ops/queues).</summary>
    public sealed class OpsDlqAlertProcessor : ThongBaoProcessor<OpsDlqAlert>
    {
        private readonly ISettings _settings;

        public OpsDlqAlertProcessor(ThongBaoWriter writer, NotificationDbContext db, ISettings settings)
            : base(writer, db)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            _settings = settings;
        }

        protected override Task XuLyPayloadAsync(OpsDlqAlert p, CancellationToken ct)
        {
            string toi = _settings.Chuoi(SettingKeys.OpsAlertEmail).Trim();
            if (toi.Length == 0)
            {
                return Task.CompletedTask;
            }

            return Writer.GhiEmailGiaoDichAsync(null, toi, "Van hanh", NoiDungThongBao.EmailCanhBaoDlq(p.Queue, p.Depth, p.Threshold), ct);
        }
    }
}
