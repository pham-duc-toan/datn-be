using System;
using System.Collections.Generic;
using Crowd.Labeling;
using Crowd.Project.Domain.Projects;

namespace Crowd.Project.Api.Dtos
{
    // Request: moi truong nullable vi client gui gi len cung duoc. Kiem tra that
    // su nam o DOMAIN (LabelingProject, LabelSchema...) — mot cho duy nhat.
    // Enum nhan/tra dang chuoi camelCase ("running").

    public sealed class CreateProjectRequest
    {
        public string? Name { get; init; }

        public string? Description { get; init; }

        /// <summary>Loai du lieu: image | text | audio | video | pair. Khong doi duoc sau khi tao.</summary>
        public string? Modality { get; init; }

        public ProjectVisibility? Visibility { get; init; }
    }

    public sealed class UpdateProjectInfoRequest
    {
        public string? Name { get; init; }

        public string? Description { get; init; }

        public ProjectVisibility? Visibility { get; init; }
    }

    public sealed class GuidelineRequest
    {
        public string? Markdown { get; init; }

        public IReadOnlyList<GuidelineExampleDto>? Examples { get; init; }
    }

    public sealed class GuidelineExampleDto
    {
        public Guid? SampleId { get; init; }

        public string? Label { get; init; }

        public bool IsCorrect { get; init; }

        public string? Explanation { get; init; }
    }

    /// <summary>Tien theo CA DU AN (docs 3.18) — ky quy tinh tu dong, khong nhap ngan sach.</summary>
    public sealed class PricingRequest
    {
        /// <summary>Thuong cho MOT nguoi lam dat ca du an.</summary>
        public long RewardVnd { get; init; }

        /// <summary>So suat (so nguoi lam bai), trong [project.min_slots, project.max_slots].</summary>
        public int Slots { get; init; }

        /// <summary>t — % cau giong da so toi thieu de dat, trong [x, y] admin dat.</summary>
        public int PassThresholdPercent { get; init; }

        public DateTimeOffset? Deadline { get; init; }
    }

    public sealed class ChannelsRequest
    {
        public bool AllowProfessional { get; init; }

        public bool AllowLinkGateway { get; init; }

        public bool AllowCollaborative { get; init; }

        /// <summary>Cong link: gia moi nhan khach vuot link gan (kenh phu, tinh theo luot).</summary>
        public long? GateUnitPriceVnd { get; init; }

        /// <summary>Cong link: ngan sach rieng (nam trong ky quy).</summary>
        public long? GateBudgetVnd { get; init; }
    }

    public sealed class EligibilityRequest
    {
        public int? MinLevel { get; init; }

        public int? MinReputation { get; init; }

        public bool RequireEntranceTest { get; init; }

        public int EntranceQuestionCount { get; init; }

        public int EntrancePassPercent { get; init; }
    }

    public sealed class ReasonRequest
    {
        public string? Reason { get; init; }
    }

    // ---- Response ----

    public sealed class GuidelineResponse
    {
        public required string Markdown { get; init; }

        public required IReadOnlyList<GuidelineExampleDto> Examples { get; init; }
    }

    /// <summary>
    /// Chi tiet du an. Cac truong danh dau "chi chu du an" la null khi nguoi
    /// xem la labeler — ngan sach va ly do bi tu choi la thong tin noi bo cua
    /// doanh nghiep.
    /// </summary>
    public sealed class ProjectResponse
    {
        public required Guid Id { get; init; }

        public required string Name { get; init; }

        public required string Description { get; init; }

        public required string Modality { get; init; }

        public required ProjectStatus Status { get; init; }

        public required ProjectVisibility Visibility { get; init; }

        /// <summary>Tap nhan dang chuan (Crowd.Labeling): {"modality":..., "tools":[...]}. null = chua dat.</summary>
        public RawJson? LabelSchema { get; init; }

        public GuidelineResponse? Guideline { get; init; }

        /// <summary>Thuong cho MOT nguoi lam dat ca du an (moi labeler lam het cac cau trong mot bai).</summary>
        public required long RewardVnd { get; init; }

        /// <summary>So suat (so nguoi lam bai).</summary>
        public required int Slots { get; init; }

        /// <summary>t — % cau giong da so toi thieu de dat (kem: khong sai cau vang nao).</summary>
        public required int PassThresholdPercent { get; init; }

        /// <summary>So cau that cua de (chot luc publish; nhap thi la so mau hien co tru cau vang).</summary>
        public required int QuestionCount { get; init; }

        /// <summary>Cac loai cong cu gan nhan cua de (classification, bbox, ...) — "dang gan nhan".</summary>
        public required IReadOnlyList<string> ToolKinds { get; init; }

        public required long GateUnitPriceVnd { get; init; }

        public required long GateBudgetVnd { get; init; }

        /// <summary>Doanh nghiep da bam "Ket thuc som" (dang cho dong thu bai).</summary>
        public DateTimeOffset? FinishRequestedAt { get; init; }

        public DateTimeOffset? GradingStartedAt { get; init; }

        public DateTimeOffset? ClosedAt { get; init; }

        public DateTimeOffset? Deadline { get; init; }

        public required bool AllowProfessional { get; init; }

        public required bool AllowLinkGateway { get; init; }

        public required bool AllowCollaborative { get; init; }

        public int? MinLevel { get; init; }

        public int? MinReputation { get; init; }

        public required bool RequireEntranceTest { get; init; }

        public required int EntranceQuestionCount { get; init; }

        public required int EntrancePassPercent { get; init; }

        public DateTimeOffset? PublishedAt { get; init; }

        /// <summary>Noi dung nhay cam (muc 2.9): frontend lam mo mac dinh, labeler phai dong y canh bao.</summary>
        public bool SensitiveContent { get; init; }

        public string? ContentWarning { get; init; }

        /// <summary>Phai ky NDA truoc khi vao (FP-01); van ban day du o GET /projects/{id}/terms.</summary>
        public bool NdaRequired { get; init; }

        public int NdaVersion { get; init; }

        /// <summary>Chi labeler da xac minh danh tinh (FP-05).</summary>
        public bool RequireVerifiedIdentity { get; init; }

        /// <summary>Phai tu dong y dieu khoan (POST /projects/{id}/consent) truoc khi vao.</summary>
        public bool ConsentRequired { get; init; }

        public IReadOnlyList<string> Domains { get; init; } = new List<string>();

        public IReadOnlyList<string> Languages { get; init; } = new List<string>();

        /// <summary>Chu du an: da dong y xoa file goc sau khi dong (muc 3.14).</summary>
        public bool DeleteFilesAfterClose { get; init; }

        public DateTimeOffset? FilesPurgedAt { get; init; }

        /// <summary>true = nguoi xem la chu du an.</summary>
        public required bool IsOwner { get; init; }

        // ---- Chi chu du an / admin ----

        public Guid? OwnerId { get; init; }

        /// <summary>Tien ky quy (= so suat x (thuong + phi) + ngan sach cong link). Chi chu du an.</summary>
        public long? EscrowVnd { get; init; }

        public int? PlatformFeePercent { get; init; }

        public string? StatusReason { get; init; }

        public DateTimeOffset? CreatedAt { get; init; }

        public DateTimeOffset? UpdatedAt { get; init; }
    }

    public sealed class ProjectListItem
    {
        public required Guid Id { get; init; }

        public required string Name { get; init; }

        public required string Modality { get; init; }

        public required ProjectStatus Status { get; init; }

        public required ProjectVisibility Visibility { get; init; }

        /// <summary>Thuong cho MOT nguoi lam dat ca du an.</summary>
        public required long RewardVnd { get; init; }

        public required int Slots { get; init; }

        public required int PassThresholdPercent { get; init; }

        public required int QuestionCount { get; init; }

        public required IReadOnlyList<string> ToolKinds { get; init; }

        public DateTimeOffset? Deadline { get; init; }

        public required bool RequireEntranceTest { get; init; }

        public required bool IsOwner { get; init; }

        public bool SensitiveContent { get; init; }

        public bool ConsentRequired { get; init; }

        public bool RequireVerifiedIdentity { get; init; }

        public IReadOnlyList<string> Domains { get; init; } = new List<string>();

        public IReadOnlyList<string> Languages { get; init; } = new List<string>();
    }

    public sealed class PagedResponse<T>
    {
        public required IReadOnlyList<T> Items { get; init; }

        public required int Page { get; init; }

        public required int PageSize { get; init; }

        public required int Total { get; init; }
    }

    /// <summary>Checklist truoc khi publish + gioi han hien hanh de frontend dung form.</summary>
    public sealed class ReadinessResponse
    {
        public required bool Ready { get; init; }

        /// <summary>Ma nhung muc con thieu (chan publish), vd "chua_co_huong_dan".</summary>
        public required IReadOnlyList<string> Missing { get; init; }

        /// <summary>Canh bao KHONG chan publish, vd "khong_co_cau_vang_kiem_tra".</summary>
        public required IReadOnlyList<string> Warnings { get; init; }

        public required int SampleCount { get; init; }

        /// <summary>So file video media-svc dang chuan bi (chua cho publish / duyet).</summary>
        public required int VideoPreparingCount { get; init; }

        /// <summary>So file video media-svc khong chuan bi duoc (file hong) — canh bao, khong chan.</summary>
        public required int VideoFailedCount { get; init; }

        /// <summary>So cau that (mau khong phai cau vang) — moi labeler lam het chung nay.</summary>
        public required int QuestionCount { get; init; }

        /// <summary>So cau vang kiem tra tron vao moi bai.</summary>
        public required int QualityGoldCount { get; init; }

        public required int EntranceGoldCount { get; init; }

        /// <summary>Phi nen tang se chot neu publish luc nay.</summary>
        public required int PlatformFeePercent { get; init; }

        /// <summary>Phi tren MOI nguoi dat.</summary>
        public required long PlatformFeePerPassVnd { get; init; }

        /// <summary>Tien ky quy = so suat x (thuong + phi) + ngan sach cong link.</summary>
        public required long EstimatedCostVnd { get; init; }

        public required ProjectLimits Limits { get; init; }
    }

    /// <summary>Gioi han admin dat (setting) — frontend gioi han o nhap lieu.</summary>
    public sealed class ProjectLimits
    {
        /// <summary>x — nguong dat thap nhat doanh nghiep duoc chon.</summary>
        public required int PassThresholdMin { get; init; }

        /// <summary>y — nguong dat cao nhat.</summary>
        public required int PassThresholdMax { get; init; }

        public required int MinSlots { get; init; }

        public required int MaxSlots { get; init; }

        public required int MaxQuestions { get; init; }

        public required long MinRewardVnd { get; init; }
    }

}
