using System;
using System.Collections.Generic;
using System.Text.Json;
using Crowd.Labeling;

namespace Crowd.Tasking.Api.Dtos
{
    // =====================================================================
    // BAI LAM (docs 3.18) — labeler lam HET cac cau cua du an, nop CA BAI.
    // =====================================================================

    /// <summary>Mot cau hoi trong bai — du de frontend dung workspace gan nhan.</summary>
    public sealed class AttemptQuestion
    {
        /// <summary>So thu tu trong bai, tu 1 (thu tu rieng cua bai nay).</summary>
        public required int Index { get; init; }

        public required Guid SampleId { get; init; }

        /// <summary>image | text | audio | video | pair | list — frontend mo workspace tuong ung.</summary>
        public required string Modality { get; init; }

        /// <summary>Link xem file qua media-svc (watermark chim = ma labeler), song ngan. null voi text / pair / list.</summary>
        public string? FileUrl { get; init; }

        public RawJson? Content { get; init; }

        public required RawJson Metadata { get; init; }

        /// <summary>Cau tra loi da luu (nhap) — null = chua lam.</summary>
        public RawJson? Answer { get; init; }

        public DateTimeOffset? AnsweredAt { get; init; }
    }

    /// <summary>Bai lam day du (bat dau / mo lai) — cau vang tron lan, frontend KHONG phan biet duoc.</summary>
    public sealed class AttemptResponse
    {
        public required Guid AttemptId { get; init; }

        public required Guid ProjectId { get; init; }

        /// <summary>inProgress | submitted | expired | cancelled</summary>
        public required string State { get; init; }

        public required DateTimeOffset StartedAt { get; init; }

        public required DateTimeOffset ExpiresAt { get; init; }

        public DateTimeOffset? SubmittedAt { get; init; }

        public required RawJson LabelSchema { get; init; }

        public required int SchemaVersion { get; init; }

        /// <summary>Thuong neu bai DAT (khi du an ket thuc).</summary>
        public required long RewardVnd { get; init; }

        public required int PassThresholdPercent { get; init; }

        public bool SensitiveContent { get; init; }

        public required int QuestionCount { get; init; }

        public required int AnsweredCount { get; init; }

        public required IReadOnlyList<AttemptQuestion> Questions { get; init; }
    }

    /// <summary>PUT /tasks/attempts/{id}/answers/{sampleId} — {"data": {...}} (nhap, chua kiem luat) hoac {"data": null} (xoa).</summary>
    public sealed class SaveAnswerRequest
    {
        public JsonElement? Data { get; init; }
    }

    public sealed class SaveAnswerResponse
    {
        public required Guid AttemptId { get; init; }

        public required Guid SampleId { get; init; }

        public required bool Answered { get; init; }

        public required int AnsweredCount { get; init; }

        public DateTimeOffset? SavedAt { get; init; }
    }

    /// <summary>Mot cau sai dinh dang khi nop (frontend nhay toi cau do).</summary>
    public sealed class InvalidAnswer
    {
        public required int Index { get; init; }

        public required Guid SampleId { get; init; }

        public required string Code { get; init; }

        public required string Message { get; init; }
    }

    public sealed class SubmitAttemptResponse
    {
        public required Guid AttemptId { get; init; }

        public required DateTimeOffset SubmittedAt { get; init; }

        public required int QuestionCount { get; init; }

        public required int AnsweredCount { get; init; }

        /// <summary>Bai nop nay lam du an du suat → dong thu bai, cham ngay.</summary>
        public required bool CollectionFinished { get; init; }
    }

    /// <summary>Tom tat mot bai (danh sach "bai cua toi", bai cua du an, khieu nai).</summary>
    public sealed class AttemptSummary
    {
        public required Guid AttemptId { get; init; }

        public required Guid ProjectId { get; init; }

        public required Guid LabelerId { get; init; }

        public required string State { get; init; }

        public required DateTimeOffset StartedAt { get; init; }

        public required DateTimeOffset ExpiresAt { get; init; }

        public DateTimeOffset? SubmittedAt { get; init; }

        public DateTimeOffset? EndedAt { get; init; }

        public required int QuestionCount { get; init; }

        public required int AnsweredCount { get; init; }

        public required long RewardVnd { get; init; }

        public required int PassThresholdPercent { get; init; }

        // ---- Sau khi cham ----

        public int? ComparableCount { get; init; }

        public int? MatchedCount { get; init; }

        public int? AgreementPercent { get; init; }

        /// <summary>Chi hien sau khi cham (truoc do labeler khong biet cau nao la vang).</summary>
        public int? GoldTotal { get; init; }

        public int? GoldCorrect { get; init; }

        public bool? Passed { get; init; }

        /// <summary>Ket qua cuoi (sau khieu nai).</summary>
        public bool? FinalPassed { get; init; }

        /// <summary>Duoc tra thuong (trong n suat — n bai dat nop som nhat). Dat ma false = het suat thuong. null = chua cham.</summary>
        public bool? Rewarded { get; init; }

        public DateTimeOffset? GradedAt { get; init; }

        // ---- Khieu nai ----

        /// <summary>none | open | accepted | rejected</summary>
        public required string AppealState { get; init; }

        public string? AppealMessage { get; init; }

        public DateTimeOffset? AppealedAt { get; init; }

        public string? AppealNote { get; init; }

        public DateTimeOffset? AppealResolvedAt { get; init; }

        public DateTimeOffset? AppealDeadline { get; init; }

        public required bool CanAppeal { get; init; }
    }

    public sealed class AppealRequest
    {
        public string? Message { get; init; }
    }

    public sealed class ResolveAppealRequest
    {
        public bool Accept { get; init; }

        public string? Note { get; init; }
    }

    /// <summary>Tinh trang suat va thu bai cua du an — ai xem du an duoc deu thay (cho du an "con x suat").</summary>
    public sealed class SlotsResponse
    {
        public required Guid ProjectId { get; init; }

        /// <summary>So suat THUONG: so nguoi duoc thuong (n bai dat nop som nhat).</summary>
        public required int Slots { get; init; }

        /// <summary>Tran so nguoi lam bai (dang lam + da nop) = Slots x he so luc publish.</summary>
        public required int MaxParticipants { get; init; }

        public required int InProgress { get; init; }

        public required int Submitted { get; init; }

        /// <summary>MaxParticipants − InProgress − Submitted: con bao nhieu cho LAM BAI.</summary>
        public required int Available { get; init; }

        public required int QuestionCount { get; init; }

        public required long RewardVnd { get; init; }

        public required int PassThresholdPercent { get; init; }

        public DateTimeOffset? Deadline { get; init; }

        /// <summary>open | finished | graded | settled</summary>
        public required string Phase { get; init; }

        public DateTimeOffset? CollectionFinishedAt { get; init; }

        public DateTimeOffset? GradedAt { get; init; }

        public DateTimeOffset? AppealDeadline { get; init; }

        /// <summary>Bai cua chinh nguoi goi (null = chua lam / khong phai labeler).</summary>
        public AttemptSummary? Mine { get; init; }
    }

    /// <summary>Tien do du an cho chu du an (FB-20).</summary>
    public sealed class ProgressResponse
    {
        public required Guid ProjectId { get; init; }

        /// <summary>So suat THUONG.</summary>
        public required int Slots { get; init; }

        /// <summary>Tran so nguoi lam bai — du tran thi dong thu bai. Ban chup cu (truoc 10/2026) khong co → 0.</summary>
        public int MaxParticipants { get; init; }

        public required int QuestionCount { get; init; }

        public required int InProgress { get; init; }

        public required int Submitted { get; init; }

        public required int Expired { get; init; }

        public required int Cancelled { get; init; }

        /// <summary>open | finished | graded | settled</summary>
        public required string Phase { get; init; }

        public string? FinishReason { get; init; }

        public DateTimeOffset? Deadline { get; init; }

        public DateTimeOffset? CollectionFinishedAt { get; init; }

        public DateTimeOffset? GradedAt { get; init; }

        public DateTimeOffset? AppealDeadline { get; init; }

        public DateTimeOffset? SettledAt { get; init; }

        public int? PassedCount { get; init; }

        public required int OpenAppeals { get; init; }

        /// <summary>Uoc tinh luc du suat theo toc do nop 24 gio gan nhat (null = chua co du lieu).</summary>
        public DateTimeOffset? EstimatedCompletionAt { get; init; }

        /// <summary>Ban chup tien do cuoi cua du an da dong va da don du lieu van hanh.</summary>
        public DateTimeOffset? FrozenAt { get; init; }
    }

    // =====================================================================
    // BAO LOI CAU HOI (FL-07)
    // =====================================================================

    /// <summary>POST /tasks/attempts/{id}/reports {"sampleId","reason","note"}.</summary>
    public sealed class ReportTaskRequest
    {
        public Guid? SampleId { get; init; }

        public string? Reason { get; init; }

        public string? Note { get; init; }
    }

    public sealed class TaskReportResponse
    {
        public required Guid Id { get; init; }

        public required Guid TaskId { get; init; }

        public required Guid SampleId { get; init; }

        public required Guid LabelerId { get; init; }

        public required string Reason { get; init; }

        public string? Note { get; init; }

        public required string State { get; init; }

        public required DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset? ResolvedAt { get; init; }

        /// <summary>So labeler KHAC NHAU da bao mau nay.</summary>
        public required int ReportsOnTask { get; init; }
    }

    public sealed class ResolveReportRequest
    {
        /// <summary>dismiss | exclude</summary>
        public string? Action { get; init; }
    }

    // =====================================================================
    // DON DU LIEU / NOI BO
    // =====================================================================

    public sealed class PurgeResponse
    {
        public required Guid ProjectId { get; init; }

        public required DateTimeOffset PurgedAt { get; init; }

        public required bool PurgedNow { get; init; }

        public required int DeletedTasks { get; init; }

        public required int DeletedAnswers { get; init; }

        public required int DeletedReports { get; init; }

        public required int DeletedGoldSamples { get; init; }
    }

    /// <summary>GET /internal/projects/{id}/close-check — project-svc hoi truoc khi HUY du an.</summary>
    public sealed class CloseCheckResponse
    {
        public required Guid ProjectId { get; init; }

        public required bool Paused { get; init; }

        public required int InProgressCount { get; init; }

        public required int SubmittedCount { get; init; }
    }
}
