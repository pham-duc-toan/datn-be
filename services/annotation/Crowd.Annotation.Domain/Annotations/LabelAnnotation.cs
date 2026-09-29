using System;
using System.Collections.Generic;
using Crowd.Annotation.Domain.Common;

namespace Crowd.Annotation.Domain.Annotations
{
    public enum AnnotationStatus
    {
        /// <summary>Vua nop, cho doanh nghiep/reviewer duyet.</summary>
        PendingReview,

        /// <summary>Duoc duyet → phat annotation.approved → ledger chi tien.</summary>
        Approved,

        /// <summary>Bi tu choi. Con khieu nai duoc neu chua khieu nai lan nao.</summary>
        Rejected,

        /// <summary>Labeler dang khieu nai, cho admin phan xu.</summary>
        Appealed,
    }

    public enum LabelSource
    {
        Professional,
        LinkGateway,
        Collaborative,
    }

    /// <summary>Mot dong nhat ky vong doi (docs muc 5: state_histories) — ai lam gi, luc nao.</summary>
    public sealed class AnnotationHistoryEntry
    {
        private AnnotationHistoryEntry()
        {
            Action = string.Empty;
        }

        public AnnotationHistoryEntry(string action, Guid? actorId, string? note, DateTimeOffset at)
        {
            Action = action;
            ActorId = actorId;
            Note = note;
            At = at;
        }

        /// <summary>"submitted" | "approved" | "rejected" | "appealed" | "appeal_accepted" | "appeal_denied"</summary>
        public string Action { get; private set; }

        public Guid? ActorId { get; private set; }

        public string? Note { get; private set; }

        public DateTimeOffset At { get; private set; }
    }

    /// <summary>
    /// AGGREGATE ROOT: mot nhan do mot nguoi gan cho mot mau.
    ///
    /// Vong doi (FB-21, FL-09):
    ///
    ///   PendingReview ──duyet──► Approved                      (chi tien)
    ///        │
    ///        └──tu choi──► Rejected ──khieu nai (≤7 ngay, 1 lan)──► Appealed
    ///                                                                  │
    ///                                   admin chap nhan ──► Approved ◄─┤ (chi tien)
    ///                                   admin bac       ──► Rejected ◄─┘ (cuoi cung)
    ///
    /// Luat tien: MOT nhan chi duoc chuyen sang Approved MOT lan trong doi — ledger
    /// con chan them bang UNIQUE(annotation_id) (VD-M-02), nhung chan tu goc van hon.
    ///
    /// Ten la LabelAnnotation: trong namespace Crowd.Annotation.*, chu "Annotation"
    /// bi hieu la namespace — cung ly do voi LabelingProject, Crowd.Tasking.
    /// </summary>
    public sealed class LabelAnnotation
    {
        public const int DoDaiLyDoToiDa = 1000;
        public static readonly TimeSpan HanKhieuNai = TimeSpan.FromDays(7);

        private List<string> _labels;
        private List<AnnotationHistoryEntry> _history;

        private LabelAnnotation()
        {
            StorageKey = string.Empty;
            _labels = new List<string>();
            _history = new List<AnnotationHistoryEntry>();
        }

        public Guid Id { get; private set; }

        /// <summary>Luot lease sinh ra nhan nay. UNIQUE: nop giao lai khong thanh hai nhan.</summary>
        public Guid AssignmentId { get; private set; }

        public Guid TaskId { get; private set; }

        public Guid ProjectId { get; private set; }

        public Guid SampleId { get; private set; }

        public string StorageKey { get; private set; }

        /// <summary>null = khach vang lai o cong link (khong ai nhan tien).</summary>
        public Guid? LabelerId { get; private set; }

        public IReadOnlyList<string> Labels
        {
            get { return _labels; }
        }

        public LabelSource Source { get; private set; }

        public AnnotationStatus Status { get; private set; }

        public DateTimeOffset SubmittedAt { get; private set; }

        public Guid? ReviewerId { get; private set; }

        public DateTimeOffset? ReviewedAt { get; private set; }

        public string? RejectReason { get; private set; }

        public string? AppealMessage { get; private set; }

        public DateTimeOffset? AppealedAt { get; private set; }

        public DateTimeOffset? AppealResolvedAt { get; private set; }

        public IReadOnlyList<AnnotationHistoryEntry> History
        {
            get { return _history; }
        }

        public static LabelAnnotation TaoTuLuotNop(
            Guid assignmentId,
            Guid taskId,
            Guid projectId,
            Guid sampleId,
            string storageKey,
            Guid labelerId,
            IReadOnlyList<string> labels,
            DateTimeOffset submittedAt)
        {
            if (labels == null || labels.Count == 0)
            {
                throw new InvalidValueException("nhan_rong", "Nhan khong duoc rong.");
            }

            LabelAnnotation a = new LabelAnnotation();
            a.Id = Guid.CreateVersion7();
            a.AssignmentId = assignmentId;
            a.TaskId = taskId;
            a.ProjectId = projectId;
            a.SampleId = sampleId;
            a.StorageKey = storageKey;
            a.LabelerId = labelerId;
            a._labels = new List<string>(labels);
            a.Source = LabelSource.Professional;
            a.Status = AnnotationStatus.PendingReview;
            a.SubmittedAt = submittedAt;
            a._history.Add(new AnnotationHistoryEntry("submitted", labelerId, null, submittedAt));
            return a;
        }

        // =====================================================================
        // DUYET (FB-21)
        // =====================================================================

        public void Duyet(Guid reviewerId, DateTimeOffset luc)
        {
            KiemTruocKhiDuyet(reviewerId, "duyet");

            Status = AnnotationStatus.Approved;
            ReviewerId = reviewerId;
            ReviewedAt = luc;
            _history.Add(new AnnotationHistoryEntry("approved", reviewerId, null, luc));
        }

        public void TuChoi(Guid reviewerId, string lyDo, DateTimeOffset luc)
        {
            KiemTruocKhiDuyet(reviewerId, "tu choi");
            string ly = BatBuocNoiDung(lyDo, "thieu_ly_do", "Tu choi phai ghi ly do (FB-21).");

            Status = AnnotationStatus.Rejected;
            ReviewerId = reviewerId;
            ReviewedAt = luc;
            RejectReason = ly;
            _history.Add(new AnnotationHistoryEntry("rejected", reviewerId, ly, luc));
        }

        // =====================================================================
        // KHIEU NAI (FL-09)
        // =====================================================================

        public bool ConKhieuNaiDuoc(DateTimeOffset luc)
        {
            return Status == AnnotationStatus.Rejected
                   && AppealMessage == null
                   && ReviewedAt.HasValue
                   && luc - ReviewedAt.Value <= HanKhieuNai;
        }

        public void KhieuNai(Guid labelerId, string noiDung, DateTimeOffset luc)
        {
            if (LabelerId == null || LabelerId.Value != labelerId)
            {
                throw new RuleViolationException("khong_phai_nhan_cua_ban", "Chi nguoi gan nhan moi khieu nai duoc.");
            }

            if (Status != AnnotationStatus.Rejected)
            {
                throw new RuleViolationException("khong_the_khieu_nai", "Chi khieu nai duoc nhan dang bi tu choi.");
            }

            if (AppealMessage != null)
            {
                throw new RuleViolationException("da_khieu_nai", "Moi nhan chi khieu nai mot lan.");
            }

            if (!ConKhieuNaiDuoc(luc))
            {
                throw new RuleViolationException("qua_han_khieu_nai", "Da qua 7 ngay ke tu luc bi tu choi.");
            }

            AppealMessage = BatBuocNoiDung(noiDung, "thieu_noi_dung", "Khieu nai phai co noi dung.");
            AppealedAt = luc;
            Status = AnnotationStatus.Appealed;
            _history.Add(new AnnotationHistoryEntry("appealed", labelerId, AppealMessage, luc));
        }

        /// <summary>
        /// Admin phan xu. Chap nhan → Approved (chi tien); bac → Rejected cuoi cung.
        /// Tra ve true neu chap nhan.
        /// </summary>
        public bool XuLyKhieuNai(Guid adminId, bool chapNhan, string? ghiChu, DateTimeOffset luc)
        {
            if (Status != AnnotationStatus.Appealed)
            {
                throw new RuleViolationException("khong_co_khieu_nai", "Nhan nay khong co khieu nai dang cho.");
            }

            AppealResolvedAt = luc;

            if (chapNhan)
            {
                Status = AnnotationStatus.Approved;
                _history.Add(new AnnotationHistoryEntry("appeal_accepted", adminId, ghiChu, luc));
                return true;
            }

            Status = AnnotationStatus.Rejected;
            _history.Add(new AnnotationHistoryEntry("appeal_denied", adminId, ghiChu, luc));
            return false;
        }

        /// <summary>Tu choi CUOI CUNG: da khieu nai va bi bac, khong con duong nao nua.</summary>
        public bool LaTuChoiCuoiCung()
        {
            return Status == AnnotationStatus.Rejected && AppealResolvedAt.HasValue;
        }

        // =====================================================================

        private void KiemTruocKhiDuyet(Guid reviewerId, string hanhDong)
        {
            if (Status != AnnotationStatus.PendingReview)
            {
                throw new RuleViolationException(
                    "da_duyet",
                    "Khong the " + hanhDong + ": nhan dang o trang thai " + Status + ".");
            }

            // Mot nguoi vua la labeler vua la reviewer cua du an khong duoc tu duyet
            // nhan cua chinh minh — tu cho minh tien.
            if (LabelerId.HasValue && LabelerId.Value == reviewerId)
            {
                throw new RuleViolationException("tu_duyet", "Khong duoc tu duyet nhan cua chinh minh.");
            }
        }

        private static string BatBuocNoiDung(string noiDung, string code, string thongBao)
        {
            string s = noiDung == null ? string.Empty : noiDung.Trim();
            if (s.Length == 0 || s.Length > DoDaiLyDoToiDa)
            {
                throw new InvalidValueException(code, thongBao + " (1-" + DoDaiLyDoToiDa + " ky tu)");
            }

            return s;
        }
    }
}
