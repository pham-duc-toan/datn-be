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

    public sealed class AddMemberRequest
    {
        public Guid? UserId { get; init; }

        public MemberRole? Role { get; init; }
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

        /// <summary>Link xem / nghe file, het han sau vai phut (S-07). null voi text / pair.</summary>
        public string? FileUrl { get; init; }

        /// <summary>Noi dung voi text / pair: {"text"} hoac {"prompt","a","b"}.</summary>
        public RawJson? Content { get; init; }

        /// <summary>width/height, durationSec, length, segmentStart/segmentEnd...</summary>
        public required RawJson Metadata { get; init; }
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
}
