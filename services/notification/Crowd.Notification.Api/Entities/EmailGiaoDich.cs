using System;

namespace Crowd.Notification.Api.Entities
{
    /// <summary>
    /// Email GIAO DICH (xac thuc email, dat lai mat khau): chi gui email, KHONG thanh thong bao trong ung
    /// dung (link chua ma mot lan — hien trong ung dung la vo nghia va lo ma). Dia chi lay thang tu event
    /// (tai khoan vua dang ky co the chua co trong bang lien he).
    /// Gui xong thi XOA noi dung (link con han trong DB la mot ban sao ma khong can thiet).
    /// </summary>
    public sealed class EmailGiaoDich
    {
        private EmailGiaoDich()
        {
            ToEmail = string.Empty;
            DisplayName = string.Empty;
            Subject = string.Empty;
            Body = string.Empty;
        }

        public Guid Id { get; private set; }

        public Guid UserId { get; private set; }

        public string ToEmail { get; private set; }

        public string DisplayName { get; private set; }

        public string Subject { get; private set; }

        public string Body { get; private set; }

        public EmailState State { get; private set; }

        public int Attempts { get; private set; }

        public DateTimeOffset? NextAt { get; private set; }

        public DateTimeOffset? SentAt { get; private set; }

        public string? Error { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public static EmailGiaoDich Tao(Guid userId, string toEmail, string displayName, string subject, string body, DateTimeOffset luc)
        {
            EmailGiaoDich e = new EmailGiaoDich();
            e.Id = Guid.CreateVersion7();
            e.UserId = userId;
            e.ToEmail = toEmail;
            e.DisplayName = displayName;
            e.Subject = subject;
            e.Body = body;
            e.State = EmailState.Pending;
            e.NextAt = luc;
            e.CreatedAt = luc;
            return e;
        }

        public void DaGui(DateTimeOffset luc)
        {
            State = EmailState.Sent;
            SentAt = luc;
            NextAt = null;
            Error = null;
            Attempts = Attempts + 1;
            Body = string.Empty;
        }

        /// <summary>Loi SMTP: hen gui lai (cung backoff voi thong bao), qua so lan thi bo va xoa noi dung.</summary>
        public void GuiLoi(string loi, int soLanToiDa, DateTimeOffset luc)
        {
            Attempts = Attempts + 1;
            Error = loi.Length > 500 ? loi.Substring(0, 500) : loi;
            if (Attempts >= soLanToiDa)
            {
                State = EmailState.Failed;
                NextAt = null;
                Body = string.Empty;
            }
            else
            {
                NextAt = luc + ThongBao.ThoiGianChoGuiLai(Attempts);
            }
        }
    }
}
