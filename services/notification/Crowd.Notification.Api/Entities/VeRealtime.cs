using System;
using System.Security.Cryptography;
using System.Text;

namespace Crowd.Notification.Api.Entities
{
    /// <summary>
    /// Ve mo WebSocket (trinh duyet khong gui duoc header Authorization khi mo WebSocket): nguoi dung da
    /// dang nhap xin ve qua API thuong (co JWT), roi mo ws://.../notifications/realtime?ticket=... Ve dung
    /// MOT lan, song notification.realtime_ticket_ttl giay, chi luu BAM SHA-256 — lo log URL cung khong dung
    /// lai duoc. Luu trong DB (khong phai bo nho) de instance phat ve va instance nhan WebSocket khac nhau.
    /// </summary>
    public sealed class VeRealtime
    {
        private VeRealtime()
        {
            TokenHash = string.Empty;
        }

        public string TokenHash { get; private set; }

        public Guid UserId { get; private set; }

        public DateTimeOffset ExpiresAt { get; private set; }

        public static VeRealtime Tao(string tokenHash, Guid userId, DateTimeOffset hetHan)
        {
            VeRealtime v = new VeRealtime();
            v.TokenHash = tokenHash;
            v.UserId = userId;
            v.ExpiresAt = hetHan;
            return v;
        }

        public static string Bam(string token)
        {
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        }

        public static string TaoToken()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
