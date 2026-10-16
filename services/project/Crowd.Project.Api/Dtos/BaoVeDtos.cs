using System;
using System.Collections.Generic;
using Crowd.Project.Domain.Members;
using Crowd.Project.Domain.Projects;

namespace Crowd.Project.Api.Dtos
{
    // ---- Bao ve: noi dung nhay cam (muc 2.9), NDA (FP-01), xac minh danh tinh (FP-05) ----

    public sealed class ProtectionRequest
    {
        public bool SensitiveContent { get; init; }

        public string? ContentWarning { get; init; }

        public bool NdaRequired { get; init; }

        public string? NdaText { get; init; }

        public bool RequireVerifiedIdentity { get; init; }
    }

    public sealed class ProjectTermsResponse
    {
        public required Guid ProjectId { get; init; }

        public required bool SensitiveContent { get; init; }

        public string? ContentWarning { get; init; }

        public required bool NdaRequired { get; init; }

        public string? NdaText { get; init; }

        public required int NdaVersion { get; init; }

        public required bool RequireVerifiedIdentity { get; init; }

        /// <summary>Phai tu dong y (canh bao / NDA) truoc khi vao.</summary>
        public required bool ConsentRequired { get; init; }

        /// <summary>Nguoi goi da dong y dieu khoan hien hanh chua.</summary>
        public required bool MyConsentValid { get; init; }

        public DateTimeOffset? MyNdaSignedAt { get; init; }

        public DateTimeOffset? MyContentWarningAcceptedAt { get; init; }
    }

    public sealed class ConsentRequest
    {
        public bool AcceptContentWarning { get; init; }

        public bool AcceptNda { get; init; }

        /// <summary>Ho ten tu go — chu ky NDA dien tu.</summary>
        public string? FullName { get; init; }
    }

    public sealed class ConsentRecordResponse
    {
        public required Guid UserId { get; init; }

        public DateTimeOffset? ContentWarningAcceptedAt { get; init; }

        public int? NdaVersion { get; init; }

        public string? NdaTextHash { get; init; }

        public string? NdaSignedName { get; init; }

        public DateTimeOffset? NdaSignedAt { get; init; }

        public string? Ip { get; init; }

        public string? UserAgent { get; init; }
    }

    // ---- Nhan goi y (FL-02) ----

    public sealed class TagsRequest
    {
        public IReadOnlyList<string>? Domains { get; init; }

        public IReadOnlyList<string>? Languages { get; init; }
    }

    public sealed class RecommendedProjectResponse
    {
        public required Guid Id { get; init; }

        public required string Name { get; init; }

        public required string Modality { get; init; }

        public required ProjectVisibility Visibility { get; init; }

        public required long UnitPriceVnd { get; init; }

        public DateTimeOffset? Deadline { get; init; }

        public required bool RequireEntranceTest { get; init; }

        public required bool SensitiveContent { get; init; }

        public required bool ConsentRequired { get; init; }

        public required IReadOnlyList<string> Domains { get; init; }

        public required IReadOnlyList<string> Languages { get; init; }

        /// <summary>Diem phu hop: loai du lieu +3, moi linh vuc trung +2, moi ngon ngu trung +2.</summary>
        public required int Score { get; init; }

        /// <summary>Ly do goi y: modality | domain:{x} | language:{x}.</summary>
        public required IReadOnlyList<string> Reasons { get; init; }
    }

    // ---- Loi moi cho ----

    public sealed class InvitationResponse
    {
        public required Guid InvitationId { get; init; }

        public required Guid ProjectId { get; init; }

        public required string ProjectName { get; init; }

        /// <summary>
        /// Chu du an xem loi moi theo userId: null (khong lo email cua nguoi do). Nguoi duoc moi xem: luon co.
        /// </summary>
        public string? Email { get; init; }

        /// <summary>
        /// Chu du an xem loi moi theo email: null cho toi khi nguoi do chap nhan — khong lo email nao co tai khoan
        /// (VD-S-16). Nguoi duoc moi xem: luon co.
        /// </summary>
        public Guid? UserId { get; init; }

        /// <summary>true = chu du an moi bang email; false = bang userId.</summary>
        public required bool ByEmail { get; init; }

        public required MemberRole Role { get; init; }

        public required InvitationStatus Status { get; init; }

        /// <summary>Phai dong y canh bao / ky NDA truoc khi chap nhan.</summary>
        public required bool RequiresConsent { get; init; }

        public required DateTimeOffset CreatedAt { get; init; }

        public required DateTimeOffset ExpiresAt { get; init; }
    }

    // ---- Danh sach uu tien / chan va danh gia (FB-23) ----

    public sealed class OwnerListRequest
    {
        /// <summary>preferred | blocked</summary>
        public OwnerListKind? Kind { get; init; }

        public string? Note { get; init; }
    }

    public sealed class OwnerListEntryResponse
    {
        public required Guid LabelerId { get; init; }

        public required OwnerListKind Kind { get; init; }

        public string? Note { get; init; }

        public required DateTimeOffset CreatedAt { get; init; }

        public required DateTimeOffset UpdatedAt { get; init; }

        /// <summary>So du an cua doanh nghiep vua chan / mo lai labeler nay (chi khi doi danh sach).</summary>
        public int? AffectedProjects { get; init; }
    }

    public sealed class RatingRequest
    {
        public int Stars { get; init; }

        public string? Comment { get; init; }
    }

    public sealed class RatingResponse
    {
        public required Guid ProjectId { get; init; }

        public required Guid LabelerId { get; init; }

        public required int Stars { get; init; }

        public string? Comment { get; init; }

        public required DateTimeOffset UpdatedAt { get; init; }
    }

    public sealed class RatingSummaryResponse
    {
        public required Guid LabelerId { get; init; }

        /// <summary>null = chua co danh gia.</summary>
        public double? Average { get; init; }

        public required int Count { get; init; }

        /// <summary>So danh gia 1..5 sao (chi so 0 = 1 sao).</summary>
        public required IReadOnlyList<int> Distribution { get; init; }
    }

    public sealed class RetentionRequest
    {
        /// <summary>true = cho phep xoa file goc khi du an da dong qua project.closed_data_retention.</summary>
        public bool DeleteFilesAfterClose { get; init; }
    }
}
