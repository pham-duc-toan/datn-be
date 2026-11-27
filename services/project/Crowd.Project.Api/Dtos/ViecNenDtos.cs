using System;

namespace Crowd.Project.Api.Dtos
{
    // ---- Tao file ket qua chay nen (kien truc 3.41) ----

    /// <summary>POST /annotations/projects/{id}/exports — {"format": "yolo"} (bo trong = json).</summary>
    public sealed class TaoFileXuatRequest
    {
        public string? Format { get; init; }
    }

    /// <summary>Mot lan tao file ket qua.</summary>
    public sealed class ExportJobResponse
    {
        public required Guid Id { get; init; }

        public required Guid ProjectId { get; init; }

        public required string Format { get; init; }

        /// <summary>queued | processing | ready | failed (dang cho qua export.job_timeout cung bao failed).</summary>
        public required string Status { get; init; }

        public string? FileName { get; init; }

        public long? SizeBytes { get; init; }

        /// <summary>So mau co ket qua trong file.</summary>
        public int? SampleCount { get; init; }

        public string? Error { get; init; }

        public required DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset? FinishedAt { get; init; }

        /// <summary>Duong tai file (qua gateway, kem token) khi ready: /annotations/projects/{p}/exports/{id}/file.</summary>
        public string? DownloadPath { get; init; }
    }

    // ---- ZIP anh chay nen (kien truc 3.41) ----

    /// <summary>POST /projects/{id}/samples/zip — {"key": "&lt;key tu /uploads&gt;", "name": "anh.zip"}.</summary>
    public sealed class DangKyZipRequest
    {
        public string? Key { get; init; }

        public string? Name { get; init; }
    }

    /// <summary>Mot phieu giai nen ZIP.</summary>
    public sealed class ZipImportResponse
    {
        public required Guid Id { get; init; }

        public required string Name { get; init; }

        public required long SizeBytes { get; init; }

        /// <summary>queued | processing | done | failed.</summary>
        public required string Status { get; init; }

        /// <summary>So file trong ZIP da doc (tang dan luc processing).</summary>
        public required int Processed { get; init; }

        /// <summary>So anh hop le thanh mau (khi done).</summary>
        public required int Ready { get; init; }

        /// <summary>So file bo qua: khong phai anh, qua lon, trung noi dung (khi done).</summary>
        public required int Skipped { get; init; }

        public string? Error { get; init; }

        public required DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset? FinishedAt { get; init; }
    }
}
