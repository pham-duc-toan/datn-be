using System;
using System.Collections.Generic;
using System.Text.Json;
using Crowd.Labeling;
using Crowd.Project.Domain.Datasets;
using Crowd.Project.Domain.Gold;
using Crowd.Project.Domain.Members;

namespace Crowd.Project.Api.Dtos
{
    // ---- Thanh vien ----

    /// <summary>Moi MOT nguoi: userId HOAC email (khong ca hai).</summary>
    public sealed class AddMemberRequest
    {
        public Guid? UserId { get; init; }

        public string? Email { get; init; }

        public MemberRole? Role { get; init; }
    }

    /// <summary>Moi NHIEU nguoi cung vai tro: userIds va / hoac emails (tong toi da project.member_batch_max).</summary>
    public sealed class AddMembersBatchRequest
    {
        public MemberRole? Role { get; init; }

        public IReadOnlyList<Guid>? UserIds { get; init; }

        public IReadOnlyList<string>? Emails { get; init; }
    }

    public sealed class AddMembersBatchResponse
    {
        /// <summary>So nguoi da them.</summary>
        public required int Added { get; init; }

        /// <summary>So loi moi cho vua gui (email chua co tai khoan / du an can tu dong y dieu khoan).</summary>
        public int Invited { get; init; }

        /// <summary>Moi dau vao mot dong, cung thu tu (userIds truoc, emails sau).</summary>
        public required IReadOnlyList<AddMemberResult> Results { get; init; }
    }

    public sealed class AddMemberResult
    {
        /// <summary>Dau vao nguyen van (userId hoac email nhu da gui).</summary>
        public required string Input { get; init; }

        public Guid? UserId { get; init; }

        public string? DisplayName { get; init; }

        /// <summary>
        /// added | khong_tim_thay | email_khong_hop_le | trung_lap | da_la_thanh_vien | bi_chan |
        /// tai_khoan_bi_khoa | khong_phai_labeler | chua_du_cap_do | chua_du_uy_tin
        /// </summary>
        public required string Status { get; init; }

        /// <summary>Co khi Status = invited | pending_consent.</summary>
        public Guid? InvitationId { get; init; }

        public string? Message { get; init; }
    }

    // ---- Ma moi ----
    public sealed class CreateInviteCodeRequest
    {
        /// <summary>null = khong gioi han luot.</summary>
        public int? MaxUses { get; init; }

        /// <summary>null = 7 ngay (khong vuot project.invite_code_max_ttl).</summary>
        public int? ExpiresInHours { get; init; }

        public string? Note { get; init; }
    }

    public sealed class InviteCodeResponse
    {
        public required Guid Id { get; init; }

        public required Guid ProjectId { get; init; }

        /// <summary>16 ky tu; nguoi dung go co gach / khoang trang / chu thuong deu duoc.</summary>
        public required string Code { get; init; }

        public string? Note { get; init; }

        public int? MaxUses { get; init; }

        public required int UsedCount { get; init; }

        public required DateTimeOffset ExpiresAt { get; init; }

        public required DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset? RevokedAt { get; init; }

        /// <summary>active | expired | exhausted | revoked</summary>
        public required string Status { get; init; }
    }

    public sealed class JoinByCodeRequest
    {
        public string? Code { get; init; }
    }

    public sealed class JoinByCodeResponse
    {
        public required Guid ProjectId { get; init; }

        public required string ProjectName { get; init; }

        public required MemberResponse Member { get; init; }
    }

    // ---- Don xin tham gia (du an che do approval) ----
    public sealed class CreateJoinRequestRequest
    {
        /// <summary>Loi nhan cho chu du an (toi da 500 ky tu).</summary>
        public string? Message { get; init; }
    }

    public sealed class RejectJoinRequestRequest
    {
        public string? Reason { get; init; }
    }

    public sealed class JoinRequestResponse
    {
        public required Guid Id { get; init; }

        public required Guid ProjectId { get; init; }

        public string? ProjectName { get; init; }

        public required Guid UserId { get; init; }

        /// <summary>Chi trong danh sach cua chu du an: ten, cap do, uy tin hien tai (null neu identity-svc khong tra loi).</summary>
        public string? DisplayName { get; init; }

        public int? Level { get; init; }

        public int? Reputation { get; init; }

        public string? Message { get; init; }

        /// <summary>pending | approved | rejected | cancelled</summary>
        public required JoinRequestStatus Status { get; init; }

        /// <summary>Don sinh tu bai test dau vao.</summary>
        public Guid? EntranceAttemptId { get; init; }

        public int? ScorePercent { get; init; }

        public required DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset? DecidedAt { get; init; }

        public string? RejectReason { get; init; }

        /// <summary>Don bi tu choi: tu luc nay moi gui duoc don moi.</summary>
        public DateTimeOffset? CanRequestAgainAt { get; init; }
    }

    public sealed class MemberResponse
    {
        public required Guid UserId { get; init; }

        public required MemberRole Role { get; init; }

        public required MemberState State { get; init; }

        public required DateTimeOffset JoinedAt { get; init; }
    }

    // ---- Dataset ----

    public sealed class DatasetResponse
    {
        public required Guid Id { get; init; }

        public required string Name { get; init; }

        /// <summary>pending / ingesting: worker dang xu ly; ready; failed (xem errorSummary).</summary>
        public required DatasetStatus Status { get; init; }

        public required int SampleCount { get; init; }

        /// <summary>File / dong bi bo qua: sai dinh dang, qua lon, trung noi dung da co.</summary>
        public required int SkippedCount { get; init; }

        /// <summary>Ly do cac dong bi bo qua (hoac ly do that bai).</summary>
        public string? ErrorSummary { get; init; }

        public required DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset? FinishedAt { get; init; }
    }

    public sealed class SampleResponse
    {
        public required Guid Id { get; init; }

        public required Guid DatasetId { get; init; }

        public required string Modality { get; init; }

        public required string OriginalName { get; init; }

        public long? SizeBytes { get; init; }

        /// <summary>Link xem / nghe BAN GOC qua media-svc, het han sau vai phut (S-07). null voi text / pair.</summary>
        public string? FileUrl { get; init; }

        /// <summary>Noi dung voi text / pair: {"text"} hoac {"prompt","a","b"}.</summary>
        public RawJson? Content { get; init; }

        /// <summary>width/height, durationSec, length, segmentStart/segmentEnd...</summary>
        public required RawJson Metadata { get; init; }

        /// <summary>Video: preparing | ready | failed (media-svc chuan bi ban chuan). null = khong can.</summary>
        public string? MediaState { get; init; }

        public string? MediaError { get; init; }
    }

    // ---- Upload thang len MinIO + manifest ----

    public sealed class CreateUploadsRequest
    {
        public IReadOnlyList<UploadFileInput>? Files { get; init; }
    }

    public sealed class UploadFileInput
    {
        /// <summary>Ten file goc, vd "cuoc-goi-01.wav" — chi de lay duoi file va doi chieu.</summary>
        public string? Name { get; init; }

        public long SizeBytes { get; init; }
    }

    public sealed class UploadSlotResponse
    {
        public required string Name { get; init; }

        /// <summary>Khoa file trong kho — dung lai trong manifest: {"file": "&lt;key&gt;"}.</summary>
        public required string Key { get; init; }

        /// <summary>Link PUT co han: curl -X PUT --upload-file &lt;file&gt; "&lt;uploadUrl&gt;".</summary>
        public required string UploadUrl { get; init; }

        public required DateTimeOffset ExpiresAt { get; init; }
    }

    public sealed class CreateManifestDatasetRequest
    {
        public string? Name { get; init; }

        /// <summary>
        /// Cac dong gui kem (toi da 1000). Theo loai du lieu:
        ///   image / audio / video: {"file":"&lt;key&gt;", "durationSec"?: 12.5, "width"?:..., "height"?:...}
        ///   text: {"text":"..."}     pair: {"prompt"?:"...", "a":"...", "b":"..."}
        /// Moi dong co the them "name" de doi chieu.
        /// </summary>
        public JsonElement? Rows { get; init; }

        /// <summary>Hoac: khoa file manifest .jsonl da upload (moi dong mot object nhu tren) — khi nhieu dong.</summary>
        public string? ManifestKey { get; init; }
    }

    // ---- Cau hoi vang ----

    public sealed class AddGoldItemsRequest
    {
        public IReadOnlyList<GoldItemInput>? Items { get; init; }
    }

    public sealed class GoldItemInput
    {
        public Guid? SampleId { get; init; }

        /// <summary>
        /// Dap an — CHI phan du lieu nhan, theo cac cong cu cua tap nhan, vd
        /// {"label":{"labelIds":["do"]}}. Loai du lieu lay theo du an.
        /// </summary>
        public JsonElement? ExpectedPayload { get; init; }

        public GoldPurpose? Purpose { get; init; }
    }

    public sealed class GoldItemResponse
    {
        public required Guid Id { get; init; }

        public required Guid SampleId { get; init; }

        /// <summary>Ra JSON dang {"taskType":..., "schemaVersion":..., "data":{...}}.</summary>
        public required LabelPayload ExpectedPayload { get; init; }

        public required GoldPurpose Purpose { get; init; }
    }

    // ---- Bai test dau vao ----

    public sealed class EntranceTestStartResponse
    {
        public required Guid AttemptId { get; init; }

        public required DateTimeOffset ExpiresAt { get; init; }

        /// <summary>Tap nhan cua du an — cho client biet tra loi theo nhung cong cu nao.</summary>
        public required RawJson LabelSchema { get; init; }

        /// <summary>Phien ban dinh dang nhan (LabelPayload).</summary>
        public required int SchemaVersion { get; init; }

        /// <summary>KHONG co dap an — dap an chi nam o server.</summary>
        public required IReadOnlyList<EntranceQuestion> Questions { get; init; }
    }

    public sealed class EntranceQuestion
    {
        public required Guid SampleId { get; init; }

        /// <summary>Link file (image / audio / video). null voi text / pair.</summary>
        public string? FileUrl { get; init; }

        public RawJson? Content { get; init; }

        public required RawJson Metadata { get; init; }
    }

    public sealed class SubmitEntranceTestRequest
    {
        public IReadOnlyList<EntranceAnswer>? Answers { get; init; }
    }

    public sealed class EntranceAnswer
    {
        public Guid? SampleId { get; init; }

        /// <summary>Cau tra loi — phan du lieu nhan theo cac cong cu, vd {"label":{"labelIds":["do"]}}.</summary>
        public JsonElement? Payload { get; init; }
    }

    public sealed class EntranceResultResponse
    {
        public required Guid AttemptId { get; init; }

        public required int ScorePercent { get; init; }

        public required int PassPercent { get; init; }

        public required bool Passed { get; init; }

        /// <summary>true = dau va vua thanh thanh vien du an.</summary>
        public required bool JoinedProject { get; init; }

        /// <summary>Du an che do approval: dau thi thanh DON XIN THAM GIA cho chu du an duyet (kem diem).</summary>
        public Guid? JoinRequestId { get; init; }

        public required int AttemptsLeft { get; init; }
    }

    public sealed class EntranceAttemptResponse
    {
        public required Guid AttemptId { get; init; }

        public required DateTimeOffset StartedAt { get; init; }

        public required DateTimeOffset ExpiresAt { get; init; }

        public DateTimeOffset? SubmittedAt { get; init; }

        public int? ScorePercent { get; init; }

        public bool? Passed { get; init; }
    }

    /// <summary>
    /// POST /projects/{id}/datasets/import — nhap tu nguon ngoai (FB-11), chi du an anh / am thanh / video.
    /// source = urls | s3 | gdrive, kem dung MOT khoi thong so tuong ung.
    /// </summary>
    public sealed class ImportDatasetRequest
    {
        public string? Name { get; init; }

        public string? Source { get; init; }

        /// <summary>source = urls: danh sach link http / https cong khai.</summary>
        public IReadOnlyList<string>? Urls { get; init; }

        public ImportS3Request? S3 { get; init; }

        public ImportGoogleDriveRequest? GoogleDrive { get; init; }
    }

    public sealed class ImportS3Request
    {
        /// <summary>Bo trong = AWS S3 that; dien de dung MinIO / dich vu tuong thich S3.</summary>
        public string? Endpoint { get; init; }

        public string? Region { get; init; }

        public string? Bucket { get; init; }

        public string? Prefix { get; init; }

        public string? AccessKey { get; init; }

        /// <summary>Chi luu dang ma hoa toi khi nhap xong roi xoa.</summary>
        public string? SecretKey { get; init; }
    }

    public sealed class ImportGoogleDriveRequest
    {
        /// <summary>Link chia se file hoac thu muc (thu muc can khoa API Google cau hinh o server).</summary>
        public IReadOnlyList<string>? Links { get; init; }
    }

    // ---- Noi bo ----

    /// <summary>GET /internal/projects/{id}/published-terms: dung payload project.published + luc publish.</summary>
    public sealed class PublishedTermsResponse
    {
        public required DateTimeOffset PublishedAt { get; init; }

        public required Crowd.Contracts.Project.ProjectPublished Terms { get; init; }
    }
}
