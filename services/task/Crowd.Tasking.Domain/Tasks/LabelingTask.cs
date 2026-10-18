using System;
using Crowd.Labeling;
using Crowd.Tasking.Domain.Common;

namespace Crowd.Tasking.Domain.Tasks
{
    public enum TaskState
    {
        /// <summary>Cau hoi that cua de — moi bai lam deu co.</summary>
        Open,

        /// <summary>Giu cho du lieu cu (truoc du an lam trung tam). Khong con dung.</summary>
        Completed,

        /// <summary>Du an bi huy / ket thuc, hoac mau bi loai (bao loi) — khong vao bai moi, khong cham.</summary>
        Cancelled,

        /// <summary>Mau la cau hoi vang — co dap an roi: vao bai nhu cau thuong nhung chi de cham.</summary>
        Excluded,
    }

    /// <summary>
    /// Mot CAU HOI cua de (docs 3.18) = mot mau du lieu. Moi bai lam deu lam het cac cau Open (cung cau
    /// vang kiem tra). Khong con lease, redundancy hay bo dem nguoi gan theo tung cau.
    ///
    /// Ten la LabelingTask chu khong phai Task: "Task" trung System.Threading.Tasks.Task.
    /// </summary>
    public sealed class LabelingTask
    {
        private LabelingTask()
        {
            Modality = string.Empty;
            Metadata = SampleMetadata.Rong.ToRawJson();
        }

        public Guid Id { get; private set; }

        public Guid ProjectId { get; private set; }

        public Guid SampleId { get; private set; }

        /// <summary>Loai du lieu cua mau (image / text / audio / video / pair / list).</summary>
        public string Modality { get; private set; }

        /// <summary>Khoa file trong MinIO — de sinh link xem. null voi text / pair / list.</summary>
        public string? StorageKey { get; private set; }

        /// <summary>Noi dung text / pair / list. null voi du lieu file.</summary>
        public RawJson? Content { get; private set; }

        /// <summary>
        /// Metadata mau (rong x cao, thoi luong, doan [start, end] cua file goc...). Gui cho labeler kem
        /// cau hoi, va dung de kiem nhan nop len (khung trong anh, doan thoi gian trong doan audio).
        /// </summary>
        public RawJson Metadata { get; private set; }

        public TaskState State { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public DateTimeOffset? CompletedAt { get; private set; }

        public static LabelingTask Tao(
            Guid projectId,
            Guid sampleId,
            string modality,
            string? storageKey,
            RawJson? content,
            RawJson metadata,
            DateTimeOffset luc)
        {
            if ((storageKey == null) == (content == null))
            {
                throw new ArgumentException("Mau phai co DUNG MOT trong hai: file (storageKey) hoac noi dung (content).");
            }

            if (metadata == null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }

            LabelingTask t = new LabelingTask();
            t.Id = Guid.CreateVersion7();
            t.ProjectId = projectId;
            t.SampleId = sampleId;
            t.Modality = modality;
            t.StorageKey = storageKey;
            t.Content = content;
            t.Metadata = metadata;
            t.State = TaskState.Open;
            t.CreatedAt = luc;
            return t;
        }

        public void Huy(DateTimeOffset luc)
        {
            if (State == TaskState.Open)
            {
                State = TaskState.Cancelled;
                CompletedAt = luc;
            }
        }

        /// <summary>Loai mau hong khoi de (FL-07: nhieu labeler bao loi, hoac chu du an xac nhan).</summary>
        public bool LoaiBo(DateTimeOffset luc)
        {
            if (State != TaskState.Open)
            {
                return false;
            }

            State = TaskState.Cancelled;
            CompletedAt = luc;
            return true;
        }

        /// <summary>Mau thanh / het la cau hoi vang (chi khi du an con nhap — project-svc khoa sua vang sau publish).</summary>
        public void LoaiTruVi(bool laCauVang)
        {
            if (laCauVang && State == TaskState.Open)
            {
                State = TaskState.Excluded;
            }
            else if (!laCauVang && State == TaskState.Excluded)
            {
                State = TaskState.Open;
            }
        }
    }
}
