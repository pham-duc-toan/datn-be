using System;
using System.Collections.Generic;
using Crowd.Labeling;

namespace Crowd.Reco.Api.Dtos
{
    /// <summary>Mot mau xem truoc tren the (P1). File qua media-svc co watermark mang ma nguoi xem.</summary>
    public sealed class FeedPreview
    {
        public required Guid SampleId { get; init; }

        public required string Modality { get; init; }

        public string? FileUrl { get; init; }

        public string? WatermarkText { get; init; }

        /// <summary>Noi dung van ban / cap / danh sach (mau khong co file). Lon qua 4 KB thi chi con Snippet.</summary>
        public RawJson? Content { get; init; }

        public string? Snippet { get; init; }

        public string? Question { get; init; }

        public required RawJson Metadata { get; init; }
    }

    /// <summary>Mot cong cu + cau hoi cua tap nhan tren the.</summary>
    public sealed class FeedTool
    {
        public required string Name { get; init; }

        public required string Kind { get; init; }

        public string? Question { get; init; }

        public required IReadOnlyList<string> Classes { get; init; }
    }

    /// <summary>
    /// Mot the trong feed cho du an. KHONG co so nguoi dang lam / da nop (kien truc 3.38) — chi so suat, tien thuong,
    /// han chot, thoi gian uoc tinh.
    /// </summary>
    public sealed class FeedCard
    {
        public required Guid Id { get; init; }

        public required string Name { get; init; }

        public required string Description { get; init; }

        public required string Modality { get; init; }

        public required long RewardVnd { get; init; }

        public required int Slots { get; init; }

        public required int QuestionCount { get; init; }

        /// <summary>Phut uoc tinh lam het bai (so cau × thoi gian moi cau cua cac bai da nop / cung loai cong cu).</summary>
        public required int EstimatedMinutes { get; init; }

        public DateTimeOffset? Deadline { get; init; }

        public required IReadOnlyList<string> ToolKinds { get; init; }

        public required IReadOnlyList<string> Domains { get; init; }

        public required IReadOnlyList<string> Languages { get; init; }

        public required bool RequireEntranceTest { get; init; }

        public required bool SensitiveContent { get; init; }

        public required bool ConsentRequired { get; init; }

        public required bool RequireVerifiedIdentity { get; init; }

        public required bool IsPrivate { get; init; }

        /// <summary>Du an moi (chua du nguoi thay).</summary>
        public required bool IsNew { get; init; }

        /// <summary>The kham pha (1 / 10): du an moi hoac loai viec chua tung lam.</summary>
        public required bool Explore { get; init; }

        /// <summary>Vi tri the trong luot luot (0, 1, 2...) — frontend gui lai kem luot hien / mo.</summary>
        public required int Position { get; init; }

        /// <summary>kiem_tot | hop_so_thich | hay_dat | lam_nhanh | moi | kham_pha — frontend doi ra chu.</summary>
        public required IReadOnlyList<string> Reasons { get; init; }

        /// <summary>Nguoi xem da THICH du an nay (3.40).</summary>
        public required bool Liked { get; init; }

        /// <summary>TOAN BO cong cu + cau hoi (luon co — trang chi tiet du an da cong khai).</summary>
        public required IReadOnlyList<FeedTool> Tools { get; init; }

        /// <summary>Toi da MOT mau xem truoc he thong tu lay (rong khi doanh nghiep tat / NDA / nhay cam / rieng tu).</summary>
        public required IReadOnlyList<FeedPreview> Previews { get; init; }
    }

    /// <summary>GET /projects/feed?cursor=&amp;pageSize=&amp;order=for_you|newest|liked</summary>
    public sealed class FeedResponse
    {
        public required IReadOnlyList<FeedCard> Items { get; init; }

        /// <summary>null = da het du an phu hop.</summary>
        public string? NextCursor { get; init; }

        /// <summary>Ma lan tra trang — frontend gui lai kem luot hien / mo (ghep du lieu hoc cho ml-svc).</summary>
        public required string RequestId { get; init; }

        public required string SessionId { get; init; }
    }

    /// <summary>
    /// Mot luot hanh vi frontend gui (theo lo). Frontend giu hang doi trong localStorage, gui moi phut (3.40): EventId do
    /// frontend sinh (gui lai lo cu khong bi dem doi), At = luc xay ra hanh vi (server chi nhan trong [24 gio truoc, 2 phut sau]).
    /// </summary>
    public sealed class RecoEventItem
    {
        public Guid? EventId { get; init; }

        public DateTimeOffset? At { get; init; }

        /// <summary>impression | open | not_interested</summary>
        public string? Kind { get; init; }

        public Guid ProjectId { get; init; }

        public int? Position { get; init; }

        public string? RequestId { get; init; }

        public string? SessionId { get; init; }
    }

    /// <summary>POST /reco/events { events: [...] } — toi da 100 luot mot lan.</summary>
    public sealed class RecoEventsRequest
    {
        public List<RecoEventItem>? Events { get; init; }
    }

    public sealed class RecoEventsResponse
    {
        public required int Accepted { get; init; }
    }

    /// <summary>PUT / DELETE /reco/likes/{projectId}.</summary>
    public sealed class LikeResponse
    {
        public required Guid ProjectId { get; init; }

        public required bool Liked { get; init; }
    }

    /// <summary>GET /reco/internal/catalog — tom tat bo nho (doi ky thuat, E2E). Khoa noi bo.</summary>
    public sealed class CatalogSummary
    {
        public required int OpenProjects { get; init; }

        public required int ProjectsWithPreview { get; init; }

        public required DateTimeOffset LoadedAt { get; init; }

        public required string ProductionModel { get; init; }

        public string? CandidateModel { get; init; }
    }
}
