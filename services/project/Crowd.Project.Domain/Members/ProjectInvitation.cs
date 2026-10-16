using System;
using Crowd.Project.Domain.Common;

namespace Crowd.Project.Domain.Members
{
    public enum InvitationStatus
    {
        /// <summary>Cho nguoi duoc moi (dang ky tai khoan / dong y dieu khoan roi chap nhan).</summary>
        Pending,

        Accepted,

        Declined,

        Revoked,

        /// <summary>Qua project.invitation_ttl.</summary>
        Expired,
    }

    /// <summary>
    /// LOI MOI CHO — khi chu du an moi ma KHONG the them thang thanh thanh vien:
    ///   - email chua co tai khoan: luu email, gui thu moi dang ky; nguoi do dang ky + XAC THUC email
    ///     (chung minh so huu dia chi) thi loi moi gan voi tai khoan moi — du an khong can dong y rieng
    ///     thi vao luon, co thi cho nguoi do chap nhan;
    ///   - du an co canh bao noi dung / NDA: chu du an khong dong y thay labeler duoc → loi moi cho
    ///     nguoi do tu dong y roi bam chap nhan.
    /// Moi (du an, email) toi da MOT loi moi dang cho (chi muc duy nhat co dieu kien).
    /// </summary>
    public sealed class ProjectInvitation
    {
        private ProjectInvitation()
        {
            Email = string.Empty;
        }

        public Guid Id { get; private set; }

        public Guid ProjectId { get; private set; }

        /// <summary>Email da chuan hoa (trim, chu thuong).</summary>
        public string Email { get; private set; }

        /// <summary>null = email chua co tai khoan (cho dang ky).</summary>
        public Guid? UserId { get; private set; }

        public MemberRole Role { get; private set; }

        public InvitationStatus Status { get; private set; }

        public Guid InvitedBy { get; private set; }

        /// <summary>
        /// Chu du an moi bang EMAIL (true) hay bang userId (false). Quyet dinh chu du an duoc thay gi (VD-S-16):
        /// moi bang email thi khong thay userId (khong biet email co tai khoan hay khong) cho toi khi nguoi do chap nhan;
        /// moi bang userId thi khong thay email cua nguoi do.
        /// </summary>
        public bool ByEmail { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public DateTimeOffset ExpiresAt { get; private set; }

        public DateTimeOffset? DecidedAt { get; private set; }

        public static ProjectInvitation Tao(
            Guid projectId, string emailChuanHoa, Guid? userId, MemberRole role, Guid nguoiMoi, bool quaEmail, DateTimeOffset luc, TimeSpan han)
        {
            if (role == MemberRole.Owner)
            {
                throw new InvalidValueException("khong_the_them_owner", "Moi du an chi co mot chu so huu.");
            }

            if (string.IsNullOrWhiteSpace(emailChuanHoa))
            {
                throw new InvalidValueException("email_khong_hop_le", "Thieu email.");
            }

            ProjectInvitation i = new ProjectInvitation();
            i.Id = Guid.CreateVersion7();
            i.ProjectId = projectId;
            i.Email = emailChuanHoa;
            i.UserId = userId;
            i.Role = role;
            i.Status = InvitationStatus.Pending;
            i.InvitedBy = nguoiMoi;
            i.ByEmail = quaEmail;
            i.CreatedAt = luc;
            i.ExpiresAt = luc + han;
            return i;
        }

        public bool ConHieuLuc(DateTimeOffset luc)
        {
            return Status == InvitationStatus.Pending && ExpiresAt > luc;
        }

        /// <summary>Email vua duoc xac thuc boi tai khoan nay → loi moi thuoc ve tai khoan do.</summary>
        public void GanTaiKhoan(Guid userId)
        {
            if (UserId.HasValue && UserId.Value != userId)
            {
                throw new RuleViolationException("loi_moi_cua_nguoi_khac", "Loi moi da gan voi tai khoan khac.");
            }

            UserId = userId;
        }

        public void ChapNhan(DateTimeOffset luc)
        {
            ChiKhiDangCho(luc);
            Status = InvitationStatus.Accepted;
            DecidedAt = luc;
        }

        public void TuChoi(DateTimeOffset luc)
        {
            ChiKhiDangCho(luc);
            Status = InvitationStatus.Declined;
            DecidedAt = luc;
        }

        public void ThuHoi(DateTimeOffset luc)
        {
            if (Status != InvitationStatus.Pending)
            {
                throw new RuleViolationException("loi_moi_da_xu_ly", "Loi moi da xu ly (" + Status + ").");
            }

            Status = InvitationStatus.Revoked;
            DecidedAt = luc;
        }

        public void HetHan(DateTimeOffset luc)
        {
            if (Status == InvitationStatus.Pending)
            {
                Status = InvitationStatus.Expired;
                DecidedAt = luc;
            }
        }

        private void ChiKhiDangCho(DateTimeOffset luc)
        {
            if (Status != InvitationStatus.Pending)
            {
                throw new RuleViolationException("loi_moi_da_xu_ly", "Loi moi da xu ly (" + Status + ").");
            }

            if (ExpiresAt <= luc)
            {
                throw new RuleViolationException("loi_moi_het_han", "Loi moi da het han.");
            }
        }
    }
}
