using System;
using System.Collections.Generic;
using System.Globalization;
using Crowd.Annotation.Domain.Common;
using Crowd.Labeling;

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
        /// <summary>Do rong cot ly do / khieu nai / ghi chu trong DB — setting khong vuot qua.</summary>
        public const int CotDbLyDo = 1000;

        // Noi dung nhan o dinh dang chung (Crowd.Labeling) — ba cot task_type,
        // schema_version, payload jsonb. Moi loai nhan mot hinh dang JSON, bang
        // khong doi khi them loai moi.
        private string _payloadTaskType;
        private int _payloadSchemaVersion;
        private string _payloadJson;
        private List<AnnotationHistoryEntry> _history;

        private LabelAnnotation()
        {
            SampleMetadata = Crowd.Labeling.SampleMetadata.Rong.ToRawJson();
            _payloadTaskType = string.Empty;
            _payloadJson = string.Empty;
            _history = new List<AnnotationHistoryEntry>();
        }

        public Guid Id { get; private set; }

        /// <summary>Luot lease sinh ra nhan nay. UNIQUE: nop giao lai khong thanh hai nhan.</summary>
        public Guid AssignmentId { get; private set; }

        public Guid TaskId { get; private set; }

        public Guid ProjectId { get; private set; }

        public Guid SampleId { get; private set; }

        /// <summary>Khoa file cua mau trong MinIO. null voi text / pair.</summary>
        public string? StorageKey { get; private set; }

        /// <summary>Noi dung text / pair cua mau — reviewer xem lai, xuat file. null voi du lieu file.</summary>
        public RawJson? SampleContent { get; private set; }

        /// <summary>Metadata mau (kich thuoc, thoi luong, doan) — de ve lai khung, xuat COCO.</summary>
        public RawJson SampleMetadata { get; private set; }

        /// <summary>null = khach vang lai o cong link (khong ai nhan tien).</summary>
        public Guid? LabelerId { get; private set; }

        /// <summary>Noi dung nhan. Dung lai tu ba cot moi lan doc — LabelPayload kiem lai dinh dang.</summary>
        public LabelPayload Payload
        {
            get { return LabelPayload.TuLuuTru(_payloadTaskType, _payloadSchemaVersion, _payloadJson); }
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

        /// <summary>
        /// Nhan nay co khop ket qua dong thuan cua quality-svc khong. null = chua co
        /// ket qua, hoac tap nhan khong co cong cu gop duoc. Chi la goi y cho nguoi duyet.
        /// </summary>
        public bool? ConsensusAgrees { get; private set; }

        /// <summary>occurredAt cua consensus.reached da ap dung — ban cu den tre bi bo qua.</summary>
        public DateTimeOffset? ConsensusAt { get; private set; }

        public IReadOnlyList<AnnotationHistoryEntry> History
        {
            get { return _history; }
        }

        /// <summary>
        /// Nhan cua khach vang lai qua trang vuot link (gate.solved): da qua cau vang, KHONG co
        /// labeler (khong ai duoc tra tien theo nhan — nguoi chia se link da duoc tra theo luot),
        /// KHONG thuoc task nao (TaskId = Guid.Empty), khong tinh vao redundancy / dong thuan.
        /// assignmentId la id tat dinh (phien, mau) — event giao lai khong tao nhan thu hai.
        /// </summary>
        public static LabelAnnotation TaoTuCongLink(
            Guid assignmentId,
            Guid projectId,
            Guid sampleId,
            string? storageKey,
            RawJson? sampleContent,
            RawJson sampleMetadata,
            LabelPayload nhan,
            DateTimeOffset submittedAt)
        {
            if (sampleMetadata == null)
            {
                throw new ArgumentNullException(nameof(sampleMetadata));
            }

            if (nhan == null)
            {
                throw new InvalidValueException("nhan_rong", "Nhan khong duoc rong.");
            }

            LabelAnnotation a = new LabelAnnotation();
            a.Id = Guid.CreateVersion7();
            a.AssignmentId = assignmentId;
            a.TaskId = Guid.Empty;
            a.ProjectId = projectId;
            a.SampleId = sampleId;
            a.StorageKey = storageKey;
            a.SampleContent = sampleContent;
            a.SampleMetadata = sampleMetadata;
            a.LabelerId = null;
            a._payloadTaskType = nhan.TaskType;
            a._payloadSchemaVersion = nhan.SchemaVersion;
            a._payloadJson = nhan.DataJson;
            a.Source = LabelSource.LinkGateway;
            a.Status = AnnotationStatus.PendingReview;
            a.SubmittedAt = submittedAt;
            a._history.Add(new AnnotationHistoryEntry("submitted", null, "Cong link", submittedAt));
            return a;
        }

        public static LabelAnnotation TaoTuLuotNop(
            Guid assignmentId,
            Guid taskId,
            Guid projectId,
            Guid sampleId,
            string? storageKey,
            RawJson? sampleContent,
            RawJson sampleMetadata,
            Guid labelerId,
            LabelPayload nhan,
            DateTimeOffset submittedAt)
        {
            if (sampleMetadata == null)
            {
                throw new ArgumentNullException(nameof(sampleMetadata));
            }

            // LabelPayload luon hop le dinh dang (chi tao duoc qua LabelPayload.Tao),
            // nen o day chi can kiem co hay khong.
            if (nhan == null)
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
            a.SampleContent = sampleContent;
            a.SampleMetadata = sampleMetadata;
            a.LabelerId = labelerId;
            a._payloadTaskType = nhan.TaskType;
            a._payloadSchemaVersion = nhan.SchemaVersion;
            a._payloadJson = nhan.DataJson;
            a.Source = LabelSource.Professional;
            a.Status = AnnotationStatus.PendingReview;
            a.SubmittedAt = submittedAt;
            a._history.Add(new AnnotationHistoryEntry("submitted", labelerId, null, submittedAt));
            return a;
        }

        // =====================================================================
        // DONG THUAN (quality-svc)
        // =====================================================================

        /// <summary>Ghi ket qua dong thuan. false = event cu hon ket qua dang co (bo qua).</summary>
        public bool GhiDongThuan(bool? agrees, DateTimeOffset occurredAt)
        {
            if (ConsensusAt.HasValue && occurredAt < ConsensusAt.Value)
            {
                return false;
            }

            ConsensusAgrees = agrees;
            ConsensusAt = occurredAt;
            return true;
        }

        // =====================================================================
        // DUYET (FB-21)
        // =====================================================================

        /// <summary>
        /// Setting annotation.auto_approve_agreed: HE THONG duyet nhan khop dong thuan
        /// (khong co reviewer — ReviewerId de trong, lich su ghi "auto_approved").
        /// </summary>
        public void TuDuyetTheoDongThuan(DateTimeOffset luc)
        {
            if (Status != AnnotationStatus.PendingReview)
            {
                throw new RuleViolationException("da_duyet", "Khong the tu duyet: nhan dang o trang thai " + Status + ".");
            }

            if (ConsensusAgrees != true)
            {
                throw new RuleViolationException("chua_khop_dong_thuan", "Chi tu duyet nhan khop dong thuan.");
            }

            Status = AnnotationStatus.Approved;
            ReviewedAt = luc;
            _history.Add(new AnnotationHistoryEntry("auto_approved", null, "Khop dong thuan", luc));
        }

        public void Duyet(Guid reviewerId, DateTimeOffset luc)
        {
            KiemTruocKhiDuyet(reviewerId, "duyet");

            Status = AnnotationStatus.Approved;
            ReviewerId = reviewerId;
            ReviewedAt = luc;
            _history.Add(new AnnotationHistoryEntry("approved", reviewerId, null, luc));
        }

        public void TuChoi(Guid reviewerId, string lyDo, DateTimeOffset luc, QuyDinhDuyetNhan quyDinh)
        {
            if (quyDinh == null)
            {
                throw new ArgumentNullException(nameof(quyDinh));
            }

            KiemTruocKhiDuyet(reviewerId, "tu choi");
            string ly = BatBuocNoiDung(lyDo, "thieu_ly_do", "Tu choi phai ghi ly do (FB-21).", quyDinh.DoDaiLyDoToiDa);

            Status = AnnotationStatus.Rejected;
            ReviewerId = reviewerId;
            ReviewedAt = luc;
            RejectReason = ly;
            _history.Add(new AnnotationHistoryEntry("rejected", reviewerId, ly, luc));
        }

        // =====================================================================
        // KHIEU NAI (FL-09)
        // =====================================================================

        public bool ConKhieuNaiDuoc(DateTimeOffset luc, TimeSpan hanKhieuNai)
        {
            return Status == AnnotationStatus.Rejected
                   && AppealMessage == null
                   && ReviewedAt.HasValue
                   && luc - ReviewedAt.Value <= hanKhieuNai;
        }

        public void KhieuNai(Guid labelerId, string noiDung, DateTimeOffset luc, QuyDinhDuyetNhan quyDinh)
        {
            if (quyDinh == null)
            {
                throw new ArgumentNullException(nameof(quyDinh));
            }

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

            if (!ConKhieuNaiDuoc(luc, quyDinh.HanKhieuNai))
            {
                throw new RuleViolationException(
                    "qua_han_khieu_nai",
                    "Da qua han khieu nai (" + quyDinh.HanKhieuNai.TotalHours.ToString("0.##", CultureInfo.InvariantCulture) + " gio) ke tu luc bi tu choi.");
            }

            AppealMessage = BatBuocNoiDung(noiDung, "thieu_noi_dung", "Khieu nai phai co noi dung.", quyDinh.DoDaiLyDoToiDa);
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

        private static string BatBuocNoiDung(string noiDung, string code, string thongBao, int doDaiToiDa)
        {
            int toiDa = Math.Min(doDaiToiDa, CotDbLyDo);
            string s = noiDung == null ? string.Empty : noiDung.Trim();
            if (s.Length == 0 || s.Length > toiDa)
            {
                throw new InvalidValueException(code, thongBao + " (1-" + toiDa + " ky tu)");
            }

            return s;
        }
    }
}
