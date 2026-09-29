using System;

namespace Crowd.Payment.Api.Settings
{
    /// <summary>Tham so chuyen tien ra. Doc tu muc "Payouts".</summary>
    public sealed class PayoutOptions
    {
        public const string SectionName = "Payouts";

        /// <summary>Worker quet lenh chuyen moi bao lau mot lan.</summary>
        public TimeSpan ChuKy { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Lenh ket o "dang gui" lau hon muc nay thi tra cuu nguoc cong. VD-M-09 de
        /// xuat 15 phut.
        /// </summary>
        public TimeSpan NguongTraCuu { get; set; } = TimeSpan.FromMinutes(15);
    }
}
