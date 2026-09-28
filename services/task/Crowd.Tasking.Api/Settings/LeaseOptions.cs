using System;

namespace Crowd.Tasking.Api.Settings
{
    /// <summary>Tham so lease (FL-06). Doc tu muc "Lease".</summary>
    public sealed class LeaseOptions
    {
        public const string SectionName = "Lease";

        /// <summary>Labeler giu task bao lau. Dac ta: 15 phut.</summary>
        public TimeSpan ThoiHan { get; set; } = TimeSpan.FromMinutes(15);

        /// <summary>Reaper quet lease qua han bao lau mot lan (docs 3.5: 30 giay).</summary>
        public TimeSpan ChuKyReaper { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Moi task con lai deu DANG bi request khac khoa trong tich tac thi thu lai
        /// bay nhieu lan truoc khi tra "het task".
        /// </summary>
        public int SoLanThuLai { get; set; } = 5;

        public TimeSpan ChoGiuaHaiLanThu { get; set; } = TimeSpan.FromMilliseconds(40);
    }
}
