namespace Crowd.BuildingBlocks.Correlation
{
    /// <summary>
    /// Ten header mang correlationId qua moi hop HTTP.
    ///
    /// correlationId sinh MOT LAN o gateway, roi di theo request qua moi service
    /// va vao moi event (EventEnvelope.CorrelationId). Loc log theo mot ma la
    /// thay ca chuoi xu ly cua mot thao tac nguoi dung (VD-D-03).
    ///
    /// Dat o day de gateway va 16 service dung CUNG mot ten — go tay o 17 noi
    /// thi som muon cung co noi go sai.
    /// </summary>
    public static class CorrelationHeaders
    {
        public const string HeaderName = "X-Correlation-Id";
    }
}
