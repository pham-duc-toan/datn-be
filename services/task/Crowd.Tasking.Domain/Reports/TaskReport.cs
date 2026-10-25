using System;
using System.Collections.Generic;
using Crowd.Tasking.Domain.Common;

namespace Crowd.Tasking.Domain.Reports
{
    public enum ReportState
    {
        Open,

        /// <summary>Chu du an xem va thay mau khong co van de.</summary>
        Dismissed,

        /// <summary>Mau bi loai khoi du an (task huy) — do chu du an hoac tu dong khi nhieu nguoi bao.</summary>
        Excluded,
    }

    /// <summary>
    /// Labeler bao mot CAU loi trong bai lam (FL-07): file khong mo duoc, noi dung khong phu hop, mo ta
    /// khong ro... — de lai ghi chu cho chu du an. Chu du an (hoac du nguoi bao) loai mau → cau do bi bo
    /// khoi phan cham. Moi bai bao mot cau toi da mot lan (UNIQUE (attempt, sample)).
    /// </summary>
    public sealed class TaskReport
    {
        public const int DoDaiGhiChuToiDa = 1000;

        /// <summary>Ly do bao loi: hong (file / noi dung khong hien), khong phu hop, khong ro, khac.</summary>
        public static readonly IReadOnlyList<string> LyDoHopLe = new string[] { "broken", "inappropriate", "unclear", "other" };

        private TaskReport()
        {
            Reason = string.Empty;
        }

        public Guid Id { get; private set; }

        public Guid AttemptId { get; private set; }

        public Guid TaskId { get; private set; }

        public Guid ProjectId { get; private set; }

        public Guid SampleId { get; private set; }

        public Guid LabelerId { get; private set; }

        public string Reason { get; private set; }

        public string? Note { get; private set; }

        public ReportState State { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public DateTimeOffset? ResolvedAt { get; private set; }

        /// <summary>null = he thong tu loai (du nguoi bao).</summary>
        public Guid? ResolvedBy { get; private set; }

        public static TaskReport Tao(
            Guid attemptId, Guid taskId, Guid projectId, Guid sampleId, Guid labelerId, string? lyDo, string? ghiChu, DateTimeOffset luc)
        {
            string ly = (lyDo ?? string.Empty).Trim().ToLowerInvariant();
            if (!((IList<string>)LyDoHopLe).Contains(ly))
            {
                throw new InvalidValueException("ly_do_khong_hop_le", "reason: broken | inappropriate | unclear | other.");
            }

            string? note = ghiChu == null ? null : ghiChu.Trim();
            if (note != null && note.Length > DoDaiGhiChuToiDa)
            {
                throw new InvalidValueException("ghi_chu_qua_dai", "Ghi chú tối đa " + DoDaiGhiChuToiDa + " ký tự.");
            }

            if (ly == "other" && string.IsNullOrEmpty(note))
            {
                throw new InvalidValueException("thieu_ghi_chu", "Lý do “khác” phải kèm ghi chú.");
            }

            TaskReport r = new TaskReport();
            r.Id = Guid.CreateVersion7();
            r.AttemptId = attemptId;
            r.TaskId = taskId;
            r.ProjectId = projectId;
            r.SampleId = sampleId;
            r.LabelerId = labelerId;
            r.Reason = ly;
            r.Note = string.IsNullOrEmpty(note) ? null : note;
            r.State = ReportState.Open;
            r.CreatedAt = luc;
            return r;
        }

        public void XuLy(ReportState ketQua, Guid? nguoiXuLy, DateTimeOffset luc)
        {
            if (ketQua == ReportState.Open)
            {
                throw new ArgumentOutOfRangeException(nameof(ketQua));
            }

            if (State != ReportState.Open)
            {
                return;
            }

            State = ketQua;
            ResolvedBy = nguoiXuLy;
            ResolvedAt = luc;
        }
    }
}
