using System;

namespace Crowd.Ledger.Api.Settings
{
    /// <summary>
    /// Tham so dong tien. Doc tu muc "Ledger". Khi co admin-svc (FM-04) cac con so
    /// nay den tu platform_config.changed.
    /// </summary>
    public sealed class LedgerOptions
    {
        public const string SectionName = "Ledger";

        /// <summary>Thoi gian treo thu lao truoc khi rut duoc. Dac ta 2.11: 3-7 ngay.</summary>
        public TimeSpan ThoiGianTreo { get; set; } = TimeSpan.FromDays(3);

        /// <summary>Worker quet khoan treo den han bao lau mot lan.</summary>
        public TimeSpan ChuKyGiaiPhong { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>So tien rut toi thieu — tranh phi chuyen khoan an het tien nho.</summary>
        public long RutToiThieuVnd { get; set; } = 50000;
    }
}
