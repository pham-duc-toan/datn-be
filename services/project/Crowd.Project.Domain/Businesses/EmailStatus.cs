using System;

namespace Crowd.Project.Domain.Businesses
{
    /// <summary>
    /// BAN SAO trang thai xac thuc email (FC-01): user.registered → chua xac thuc, user.email_verified
    /// → da xac thuc. Chu du an chua xac thuc email thi chua publish duoc (tien bat dau bi khoa vao
    /// ky quy). Khong co dong = tai khoan co tu truoc tinh nang nay / tai khoan seed → coi nhu da
    /// xac thuc.
    /// </summary>
    public sealed class EmailStatus
    {
        private EmailStatus()
        {
        }

        public Guid UserId { get; private set; }

        public bool Verified { get; private set; }

        public static EmailStatus ChuaXacThuc(Guid userId)
        {
            EmailStatus e = new EmailStatus();
            e.UserId = userId;
            return e;
        }

        public void XacThuc()
        {
            Verified = true;
        }
    }
}
