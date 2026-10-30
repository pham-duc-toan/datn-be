using Crowd.BuildingBlocks.Messaging;

namespace Crowd.Contracts.Admin
{
    /// <summary>
    /// Admin gop the chu de FromCode vao ToCode (vd "y-khoa" vao "y-te"). identity-svc (ho so ky nang) va project-svc
    /// (nhan du an) thay ma cu bang ma moi, bo trung — goi y du an tiep tuc khop. Doi ten / an the KHONG phat event:
    /// noi khac chi luu ma, ten hien thi doc tu danh muc cua admin-svc.
    /// </summary>
    public sealed record TagMerged : IEventPayload
    {
        public static string EventType
        {
            get { return "tag.merged"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        /// <summary>domain | language (Crowd.BuildingBlocks.Tags.MaThe).</summary>
        public required string Kind { get; init; }

        public required string FromCode { get; init; }

        public required string ToCode { get; init; }
    }
}
