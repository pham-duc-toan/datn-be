using System;

namespace Crowd.Notification.Api.Entities
{
    /// <summary>Nguoi dung tat / bat EMAIL cho mot loai thong bao. Khong co dong = mac dinh cua loai.</summary>
    public sealed class TuyChonEmail
    {
        private TuyChonEmail()
        {
            Type = string.Empty;
        }

        public Guid UserId { get; private set; }

        public string Type { get; private set; }

        public bool EmailEnabled { get; private set; }

        public static TuyChonEmail Tao(Guid userId, string loai, bool bat)
        {
            TuyChonEmail t = new TuyChonEmail();
            t.UserId = userId;
            t.Type = loai;
            t.EmailEnabled = bat;
            return t;
        }

        public void Dat(bool bat)
        {
            EmailEnabled = bat;
        }
    }
}
