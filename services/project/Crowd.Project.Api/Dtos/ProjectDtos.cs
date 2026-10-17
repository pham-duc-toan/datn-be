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

    public sealed class PricingRequest
    {
        public long UnitPriceVnd { get; init; }

        public int Redundancy { get; init; }

        public long BudgetVnd { get; init; }

        public DateTimeOffset? Deadline { get; init; }

        /// <summary>
        /// Tran redundancy thich ung (>= redundancy). Bo trong = bang redundancy (tat
        /// thich ung). Ky quy toi thieu tinh theo tran nay.
        /// </summary>
        public int? MaxRedundancy { get; init; }
    }

    public sealed class ChannelsRequest
    {
        public bool AllowProfessional { get; init; }

        public bool AllowLinkGateway { get; init; }

        public bool AllowCollaborative { get; init; }
    }

    public sealed class EligibilityRequest
    {
        public int? MinLevel { get; init; }

        public int? MinReputation { get; init; }

        public bool RequireEntranceTest { get; init; }

        public int EntranceQuestionCount { get; init; }

        public int EntrancePassPercent { get; init; }
    }

    public sealed class QualityControlRequest
    {
        /// <summary>Phan tram so lan cap task la cau vang kiem tra, 0-50.</summary>
        public int GoldCheckPercent { get; init; }

        /// <summary>Co can duyet tung nhan khong. Bo trong = giu nguyen.</summary>
        public bool? RequireReview { get; init; }
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

        public required long UnitPriceVnd { get; init; }

        public required int Redundancy { get; init; }

        /// <summary>Tran redundancy thich ung (= redundancy khi khong bat).</summary>
        public required int MaxRedundancy { get; init; }

        /// <summary>Phan tram cau vang kiem tra tron vao luong task.</summary>
        public required int GoldCheckPercent { get; init; }

        /// <summary>false = nhan khop dong thuan duoc tu duyet (khong can duyet tung nhan).</summary>
        public required bool RequireReview { get; init; }

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

        public long? BudgetVnd { get; init; }

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

        public required long UnitPriceVnd { get; init; }

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

    /// <summary>Checklist truoc khi publish.</summary>
    public sealed class ReadinessResponse
    {
        public required bool Ready { get; init; }

        /// <summary>Ma nhung muc con thieu, vd "chua_co_huong_dan".</summary>
        public required IReadOnlyList<string> Missing { get; init; }

        public required int SampleCount { get; init; }

        public required int EntranceGoldCount { get; init; }

        /// <summary>Phi nen tang se chot neu publish luc nay.</summary>
        public required int PlatformFeePercent { get; init; }

        public required long PlatformFeePerLabelVnd { get; init; }

        /// <summary>
        /// SampleCount x Redundancy x (UnitPrice + PlatformFeePerLabel) — so tien ky
        /// quy toi thieu (dac ta 2.11). Ngan sach phai >= con so nay.
        /// </summary>
        public required long EstimatedCostVnd { get; init; }
    }
}
