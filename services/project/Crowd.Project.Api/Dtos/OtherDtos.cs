using System;
using System.Collections.Generic;
using System.Text.Json;
using Crowd.Labeling;
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

        public required int SampleCount { get; init; }

        /// <summary>File bi bo qua: sai dinh dang, qua lon, trung anh da co.</summary>
        public required int SkippedCount { get; init; }

        public required DateTimeOffset CreatedAt { get; init; }
    }

    public sealed class SampleResponse
    {
        public required Guid Id { get; init; }

        public required Guid DatasetId { get; init; }

        public required string OriginalName { get; init; }

        public required long SizeBytes { get; init; }

        /// <summary>Link xem anh, het han sau vai phut (S-07).</summary>
        public required string ImageUrl { get; init; }
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
        /// Dap an — CHI phan du lieu nhan, vd {"labelIds":["do"]}. Loai nhan lay
        /// theo du an, client khong phai gui.
        /// </summary>
        public JsonElement? ExpectedPayload { get; init; }

        /// <summary>Phien ban dinh dang nhan. Bo trong = phien ban moi nhat.</summary>
        public int? SchemaVersion { get; init; }

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

        /// <summary>Loai nhan + phien ban — cho client biet tra loi theo hinh dang JSON nao.</summary>
        public required string TaskType { get; init; }

        public required int SchemaVersion { get; init; }

        public required IReadOnlyList<string> LabelClasses { get; init; }

        public required bool AllowMultiple { get; init; }

        /// <summary>KHONG co dap an — dap an chi nam o server.</summary>
        public required IReadOnlyList<EntranceQuestion> Questions { get; init; }
    }

    public sealed class EntranceQuestion
    {
        public required Guid SampleId { get; init; }

        public required string ImageUrl { get; init; }
    }

    public sealed class SubmitEntranceTestRequest
    {
        public IReadOnlyList<EntranceAnswer>? Answers { get; init; }
    }

    public sealed class EntranceAnswer
    {
        public Guid? SampleId { get; init; }

        /// <summary>Cau tra loi — phan du lieu nhan, vd {"labelIds":["do"]}.</summary>
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
