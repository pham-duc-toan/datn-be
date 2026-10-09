using System;

namespace Crowd.Gate.Api.Helpers
{
    /// <summary>
    /// Muc "Gate" — phan HA TANG va BI MAT cua gate-svc (chuoi ket noi, khoa ky token,
    /// khoa Turnstile). Tham so nghiep vu (so cau, dem nguoc, TTL...) la setting he thong.
    /// </summary>
    public sealed class GateOptions
    {
        public const string SectionName = "Gate";

        /// <summary>vd "localhost:6381" (redis-gate).</summary>
        public string Redis { get; set; } = string.Empty;

        /// <summary>vd "Host=localhost;Port=8123;Username=gate_user;Password=...;Database=gate_db".</summary>
        public string ClickHouse { get; set; } = string.Empty;

        /// <summary>Khoa HMAC ky token mo link (>= 32 byte). Chi gate-svc biet — token chi gate kiem.</summary>
        public string TokenSigningKey { get; set; } = string.Empty;

        public TurnstileOptions Turnstile { get; set; } = new TurnstileOptions();
    }

    /// <summary>
    /// Cloudflare Turnstile — chan bot re tien TRUOC lop gan nhan (VD-L-06). Dev dung cap
    /// khoa test cua Cloudflare (luon dat): site 1x00000000000000000000AA,
    /// secret 1x0000000000000000000000000000000AA, token dummy XXXX.DUMMY.TOKEN.XXXX.
    /// </summary>
    public sealed class TurnstileOptions
    {
        public bool Enabled { get; set; } = true;

        public string SiteKey { get; set; } = string.Empty;

        public string SecretKey { get; set; } = string.Empty;
    }
}
