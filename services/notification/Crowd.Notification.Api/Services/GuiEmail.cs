using System;
using System.Net;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Crowd.Notification.Api.Services
{
    /// <summary>Cau hinh SMTP (muc "Smtp"). Dev / demo: Mailpit (localhost:1025, xem thu tai :8025).</summary>
    public sealed class SmtpOptions
    {
        public const string SectionName = "Smtp";

        public string Host { get; set; } = "localhost";

        public int Port { get; set; } = 1025;

        public bool EnableSsl { get; set; }

        public string UserName { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;
    }

    /// <summary>Gui MOT email. Tach interface de test thay bang ban gia.</summary>
    public interface IGuiEmail
    {
        Task GuiAsync(string tu, string den, string tenNguoiNhan, string tieuDe, string noiDung, CancellationToken ct);
    }

    public sealed class SmtpGuiEmail : IGuiEmail
    {
        private readonly SmtpOptions _options;

        public SmtpGuiEmail(IOptions<SmtpOptions> options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            _options = options.Value;
        }

        public async Task GuiAsync(string tu, string den, string tenNguoiNhan, string tieuDe, string noiDung, CancellationToken ct)
        {
            using (MailMessage thu = new MailMessage())
            using (SmtpClient smtp = new SmtpClient(_options.Host, _options.Port))
            {
                thu.From = new MailAddress(tu);
                thu.To.Add(new MailAddress(den, tenNguoiNhan));
                thu.Subject = tieuDe;
                thu.Body = noiDung;
                thu.IsBodyHtml = false;
                thu.BodyEncoding = System.Text.Encoding.UTF8;
                thu.SubjectEncoding = System.Text.Encoding.UTF8;

                smtp.EnableSsl = _options.EnableSsl;
                if (!string.IsNullOrEmpty(_options.UserName))
                {
                    smtp.Credentials = new NetworkCredential(_options.UserName, _options.Password);
                }

                await smtp.SendMailAsync(thu, ct);
            }
        }
    }
}
