namespace Crowd.BuildingBlocks.Security
{
    /// <summary>
    /// Muc "Privacy" — bi mat, nen o appsettings / bien moi truong, KHONG o setting he thong
    /// (ai doc duoc bang settings cung khong duoc biet muoi).
    /// </summary>
    public sealed class PrivacyOptions
    {
        public const string SectionName = "Privacy";

        /// <summary>Muoi bam IP. link-svc va gate-svc PHAI dung cung gia tri.</summary>
        public string IpSalt { get; set; } = string.Empty;
    }
}
