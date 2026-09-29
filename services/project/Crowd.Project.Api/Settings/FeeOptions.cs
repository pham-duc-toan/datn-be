namespace Crowd.Project.Api.Settings
{
    /// <summary>
    /// Phi nen tang. Doc tu muc "Fees".
    ///
    /// Tam dat trong appsettings; khi co admin-svc (P6) thi admin doi phi va phat
    /// platform_config.changed. Du an nao cung CHOT muc phi luc publish, nen doi
    /// phi chi anh huong du an publish SAU do.
    /// </summary>
    public sealed class FeeOptions
    {
        public const string SectionName = "Fees";

        /// <summary>Phan tram CONG THEM tren don gia (VD-M-15). Dac ta vi du 30%.</summary>
        public int PlatformFeePercent { get; set; } = 30;
    }
}
