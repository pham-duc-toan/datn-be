using System;
using System.Collections.Generic;

namespace Crowd.Identity.Api.Dtos
{
    // ---- Xac thuc hai lop (FC-04) ----

    public sealed class TwoFactorStatusResponse
    {
        public required bool Enabled { get; init; }

        public DateTimeOffset? EnabledAt { get; init; }

        /// <summary>So ma du phong con dung duoc (sap het thi nhac nguoi dung tao lai).</summary>
        public required int RecoveryCodesLeft { get; init; }
    }

    public sealed class TwoFactorSetupResponse
    {
        /// <summary>Bi mat dang base32 — nguoi dung nhap tay neu khong quet duoc QR.</summary>
        public required string Secret { get; init; }

        /// <summary>otpauth://totp/... — frontend ve thanh ma QR.</summary>
        public required string OtpauthUri { get; init; }
    }

    public sealed class TwoFactorCodeRequest
    {
        public string? Code { get; init; }
    }

    public sealed class TwoFactorDisableRequest
    {
        public string? Password { get; init; }

        public string? Code { get; init; }

        public string? RecoveryCode { get; init; }
    }

    public sealed class TwoFactorRecoveryCodesResponse
    {
        /// <summary>Hien MOT lan duy nhat — server chi giu ban bam.</summary>
        public required IReadOnlyList<string> RecoveryCodes { get; init; }
    }

    // ---- Xac minh danh tinh ca nhan (muc 2.9, FP-05) ----

    public sealed class IdentityVerificationResponse
    {
        public required Guid UserId { get; init; }

        /// <summary>draft | submitted | verified | rejected</summary>
        public required string Status { get; init; }

        public required string FullName { get; init; }

        public DateOnly? DateOfBirth { get; init; }

        /// <summary>4 so cuoi giay to (so day du khong bao gio duoc tra ra).</summary>
        public string? IdNumberLast4 { get; init; }

        public required bool HasFront { get; init; }

        public required bool HasBack { get; init; }

        /// <summary>Link xem anh mat truoc (song ngan) — chi tra cho CHINH CHU o GET /me/identity-verification.</summary>
        public string? FrontUrl { get; init; }

        /// <summary>Link xem anh mat sau, nhu FrontUrl.</summary>
        public string? BackUrl { get; init; }

        /// <summary>Da xac minh va du tuoi (identity.adult_age) tinh toi hom nay.</summary>
        public required bool Adult { get; init; }

        public string? RejectReason { get; init; }

        public DateTimeOffset? SubmittedAt { get; init; }

        public DateTimeOffset? ReviewedAt { get; init; }
    }

    public sealed class UpdateIdentityVerificationRequest
    {
        public string? FullName { get; init; }

        public DateOnly? DateOfBirth { get; init; }

        /// <summary>So CCCD / CMND / ho chieu. Server chi giu ban bam co khoa + 4 so cuoi.</summary>
        public string? IdNumber { get; init; }
    }

    public sealed class IdentityDocumentUploadRequest
    {
        /// <summary>front | back</summary>
        public string? Side { get; init; }

        public string? FileName { get; init; }

        public long SizeBytes { get; init; }
    }

    public sealed class IdentityVerificationAdminResponse
    {
        public required IdentityVerificationResponse Verification { get; init; }

        public required string Email { get; init; }

        public required string DisplayName { get; init; }

        /// <summary>Tuoi tinh tu ngay sinh da khai.</summary>
        public int? Age { get; init; }

        /// <summary>Link xem anh (song storage.view_link_ttl), chi o man chi tiet.</summary>
        public string? FrontUrl { get; init; }

        public string? BackUrl { get; init; }

        /// <summary>
        /// Tai khoan KHAC da duoc xac minh bang cung giay to nay (VD-T-04) — duyet se bi tu choi
        /// (409 giay_to_da_dung).
        /// </summary>
        public Guid? DuplicateOfUserId { get; init; }
    }
}
