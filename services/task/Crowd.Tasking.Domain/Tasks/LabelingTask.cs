using System;
using Crowd.Labeling;
using Crowd.Tasking.Domain.Common;

namespace Crowd.Tasking.Domain.Tasks
{
    public enum TaskState
    {
        /// <summary>Con cho nhan them nguoi gan.</summary>
        Open,

        /// <summary>Du so nguoi gan (redundancy). Khong cap them.</summary>
        Completed,

        /// <summary>Du an bi huy/ket thuc khi task chua xong.</summary>
        Cancelled,

        /// <summary>Mau la cau hoi vang — co dap an roi, khong tra tien gan nhan.</summary>
        Excluded,
    }

    /// <summary>
    /// Mot task = mot mau can RedundancyTarget nguoi gan doc lap.
    ///
    /// Hai bo dem:
    ///   ActiveLeaseCount — so nguoi DANG giu task (chua nop, chua het han).
    ///   SubmittedCount   — so nguoi DA nop.
    /// Cap them duoc khi Active + Submitted &lt; Target: khong bao gio phat qua
    /// so luot da ky quy (VD-M-03 phia task-svc).
    ///
    /// Ten la LabelingTask chu khong phai Task: "Task" trung System.Threading.Tasks.Task.
    ///
    /// Moi thay doi bo dem xay ra khi DANG GIU KHOA DONG (SELECT ... FOR UPDATE)
    /// — service lo viec khoa, entity lo luat.
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

        /// <summary>Loai du lieu cua mau (image / text / audio / video / pair).</summary>
        public string Modality { get; private set; }

        /// <summary>Khoa file trong MinIO — de sinh link xem khi cap task. null voi text / pair.</summary>
        public string? StorageKey { get; private set; }

        /// <summary>Noi dung text / pair. null voi du lieu file.</summary>
        public RawJson? Content { get; private set; }

        /// <summary>
        /// Metadata mau (rong x cao, thoi luong, doan [start, end] cua file goc...).
        /// Gui cho labeler cung task, va dung de kiem nhan nop len (khung trong anh,
        /// doan thoi gian trong doan audio).
        /// </summary>
        public RawJson Metadata { get; private set; }

        public TaskState State { get; private set; }

        /// <summary>0 = chua biet (du an chua publish).</summary>
        public int RedundancyTarget { get; private set; }

        public int ActiveLeaseCount { get; private set; }

        public int SubmittedCount { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public DateTimeOffset? CompletedAt { get; private set; }

        public static LabelingTask Tao(
            Guid projectId,
            Guid sampleId,
            string modality,
            string? storageKey,
            RawJson? content,
            RawJson metadata,
            int redundancyTarget,
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
            t.RedundancyTarget = redundancyTarget;
            t.CreatedAt = luc;
            return t;
        }

        public bool CoTheCapThem()
        {
            return State == TaskState.Open
                   && RedundancyTarget > 0
                   && ActiveLeaseCount + SubmittedCount < RedundancyTarget;
        }

        public void GhiNhanLease()
        {
            if (!CoTheCapThem())
            {
                throw new RuleViolationException("task_da_du_nguoi", "Task da du nguoi gan.");
            }

            ActiveLeaseCount = ActiveLeaseCount + 1;
        }

        /// <summary>
        /// Mot luot lease ket thuc KHONG nop (bo qua, het han, bi thu hoi): tra
        /// cho ve pool de nguoi khac nhan.
        /// </summary>
        public void TraLease()
        {
            if (ActiveLeaseCount > 0)
            {
                ActiveLeaseCount = ActiveLeaseCount - 1;
            }
        }

        /// <summary>Mot luot lease ket thuc BANG NOP. Tra ve true neu vua du redundancy.</summary>
        public bool GhiNhanNop(DateTimeOffset luc)
        {
            if (ActiveLeaseCount > 0)
            {
                ActiveLeaseCount = ActiveLeaseCount - 1;
            }

            SubmittedCount = SubmittedCount + 1;

            if (State == TaskState.Open && SubmittedCount >= RedundancyTarget)
            {
                State = TaskState.Completed;
                CompletedAt = luc;
                return true;
            }

            return false;
        }

        public void Huy(DateTimeOffset luc)
        {
            if (State == TaskState.Open)
            {
                State = TaskState.Cancelled;
                CompletedAt = luc;
            }
        }

        /// <summary>Mau thanh cau hoi vang: bo khoi pool neu chua ai dung toi.</summary>
        public void LoaiTruVi(bool laCauVang)
        {
            if (laCauVang && State == TaskState.Open && ActiveLeaseCount == 0 && SubmittedCount == 0)
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
