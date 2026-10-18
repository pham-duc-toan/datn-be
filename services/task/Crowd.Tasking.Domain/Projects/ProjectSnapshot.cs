using System;
using Crowd.Tasking.Domain.Common;
using Crowd.Labeling;

namespace Crowd.Tasking.Domain.Projects
{
    public enum SnapshotStatus
    {
        /// <summary>Moi biet du an qua dataset.ingested, chua nhan project.published.</summary>
        NotPublished,
        Running,
        Paused,

        /// <summary>Hoan thanh hoac bi huy. Trang thai cuoi — khong event nao mo lai.</summary>
        Closed,
    }

    /// <summary>
    /// BAN SAO READ-ONLY cua du an, dung tu event cua project-svc (luat 2, muc 4
    /// docs). task-svc KHONG BAO GIO goi HTTP sang project-svc de hoi "du an co
    /// dang chay khong" — no doc ban sao nay trong database cua chinh no.
    ///
    /// Event co the den SAI THU TU (VD-D-05): project.paused toi truoc
    /// project.published neu hai event nam o hai queue khac nhau. Nen:
    ///   - Cau hinh (tap nhan, gia, redundancy) chi den tu project.published va
    ///     BAT BIEN sau publish → luc nao nhan cung ap dung.
    ///   - Trang thai chi ghi de khi event MOI HON event trang thai da ap dung
    ///     (so occurredAt). Closed la diem cuoi, khong gi mo lai duoc.
    /// </summary>
    public sealed class ProjectSnapshot
    {
        // Tap nhan dang chuan (Crowd.Labeling), chep nguyen tu project.published.
        // Luu chuoi JSON (cot jsonb); doc thanh LabelSchema mot lan roi giu lai.
        private string? _labelSchemaJson;
        private LabelSchema? _labelSchema;

        private ProjectSnapshot()
        {
            Modality = string.Empty;
        }

        public Guid ProjectId { get; private set; }

        public Guid OwnerId { get; private set; }

        public SnapshotStatus Status { get; private set; }

        /// <summary>occurredAt cua event trang thai gan nhat da ap dung.</summary>
        public DateTimeOffset StatusChangedAt { get; private set; }

        /// <summary>false = chua nhan project.published, chua co cau hinh de cap task.</summary>
        public bool IsConfigured { get; private set; }

        /// <summary>Loai du lieu (image / text / audio / video / pair). Rong khi chua publish.</summary>
        public string Modality { get; private set; }

        /// <summary>
        /// Tap nhan cua du an. Nhan labeler nop duoc kiem theo no (cong cu, lop,
        /// khung nam trong anh...) — client chi gui phan du lieu. null khi chua publish.
        /// </summary>
        public LabelSchema? LabelSchema
        {
            get
            {
                if (_labelSchema == null && _labelSchemaJson != null)
                {
                    _labelSchema = LabelSchema.Doc(_labelSchemaJson);
                }

                return _labelSchema;
            }
        }

        /// <summary>Thuong cho MOT nguoi lam dat ca du an (docs 3.18).</summary>
        public long RewardVnd { get; private set; }

        /// <summary>So suat: toi da bay nhieu bai (dang lam + da nop).</summary>
        public int Slots { get; private set; }

        /// <summary>t — % cau giong da so toi thieu de dat.</summary>
        public int PassThresholdPercent { get; private set; }

        public int QuestionCount { get; private set; }

        // ---- Thu bai → cham → khieu nai → chot (docs 3.18) ----

        /// <summary>Luc dong thu bai (du suat / het han / ket thuc som). null = con nhan bai.</summary>
        public DateTimeOffset? CollectionFinishedAt { get; private set; }

        /// <summary>du_suat | het_han | ket_thuc_som</summary>
        public string? FinishReason { get; private set; }

        /// <summary>Luc quality-svc cham xong (project.graded).</summary>
        public DateTimeOffset? GradedAt { get; private set; }

        /// <summary>Han khieu nai = luc cham + task.appeal_window.</summary>
        public DateTimeOffset? AppealDeadline { get; private set; }

        /// <summary>Luc chot so nguoi dat (phat project.settled).</summary>
        public DateTimeOffset? SettledAt { get; private set; }

        public int? PassedCount { get; private set; }

        /// <summary>Luc tam dung gan nhat (de cong bu thoi gian giu bai khi chay tiep). null = khong tam dung.</summary>
        public DateTimeOffset? PausedAt { get; private set; }

        public DateTimeOffset? Deadline { get; private set; }

        public bool AllowProfessional { get; private set; }

        public bool IsPrivate { get; private set; }

        public int? MinLevel { get; private set; }

        public int? MinReputation { get; private set; }

        /// <summary>Noi dung nhay cam (muc 2.9): ep nghi giua phien, frontend lam mo mac dinh.</summary>
        public bool SensitiveContent { get; private set; }

        /// <summary>occurredAt cua gold_set.updated gan nhat da ap dung.</summary>
        public DateTimeOffset GoldSetAt { get; private set; }

        /// <summary>
        /// Tien do CUOI CUNG (JSON cua ProgressResponse), chup lai TRUOC khi xoa task / luot giu cua du an
        /// da dong. Co gia tri = da bat dau don; tu do API tien do tra ban chup nay thay vi dem bang tasks.
        /// </summary>
        public string? FinalProgressJson { get; private set; }

        /// <summary>Luc xoa XONG du lieu van hanh cua du an. null = chua don (hoac dang don do, lan sau don tiep).</summary>
        public DateTimeOffset? DataPurgedAt { get; private set; }

        /// <summary>Du an vua duoc nhac toi lan dau (thuong la qua dataset.ingested).</summary>
        public static ProjectSnapshot TaoChuaPublish(Guid projectId, DateTimeOffset occurredAt)
        {
            ProjectSnapshot s = new ProjectSnapshot();
            s.ProjectId = projectId;
            s.Status = SnapshotStatus.NotPublished;

            // MinValue: moi event trang thai that deu "moi hon" moc nay.
            s.StatusChangedAt = DateTimeOffset.MinValue;
            s.GoldSetAt = DateTimeOffset.MinValue;
            return s;
        }

        public void ApDungPublished(
            Guid ownerId,
            LabelSchema labelSchema,
            long rewardVnd,
            int slots,
            int passThresholdPercent,
            int questionCount,
            DateTimeOffset deadline,
            bool allowProfessional,
            bool isPrivate,
            int? minLevel,
            int? minReputation,
            bool sensitiveContent,
            DateTimeOffset occurredAt)
        {
            if (labelSchema == null)
            {
                throw new ArgumentNullException(nameof(labelSchema));
            }

            OwnerId = ownerId;
            Modality = labelSchema.Modality;
            _labelSchemaJson = labelSchema.ToRawJson().Json;
            _labelSchema = labelSchema;
            RewardVnd = rewardVnd;
            Slots = slots;
            PassThresholdPercent = passThresholdPercent;
            QuestionCount = questionCount;
            Deadline = deadline;
            AllowProfessional = allowProfessional;
            IsPrivate = isPrivate;
            MinLevel = minLevel;
            MinReputation = minReputation;
            SensitiveContent = sensitiveContent;
            IsConfigured = true;

            DoiTrangThaiNeuMoiHon(SnapshotStatus.Running, occurredAt);
        }

        /// <summary>Tra ve false neu event cu hon trang thai hien tai (bi bo qua).</summary>
        public bool TamDung(DateTimeOffset occurredAt)
        {
            bool doi = DoiTrangThaiNeuMoiHon(SnapshotStatus.Paused, occurredAt);
            if (doi)
            {
                PausedAt = occurredAt;
            }

            return doi;
        }

        /// <summary>
        /// Chay tiep. Tra ve thoi gian DA tam dung (de cong bu cho bai dang lam), null khi event cu
        /// (bo qua) hoac khong biet luc tam dung.
        /// </summary>
        public TimeSpan? TiepTuc(DateTimeOffset occurredAt)
        {
            DateTimeOffset? luc = PausedAt;
            if (!DoiTrangThaiNeuMoiHon(SnapshotStatus.Running, occurredAt))
            {
                return null;
            }

            PausedAt = null;
            if (!luc.HasValue || occurredAt <= luc.Value)
            {
                return TimeSpan.Zero;
            }

            return occurredAt - luc.Value;
        }

        /// <summary>Con nhan bai (bat dau / nop) khong: dang chay, chua dong thu bai, chua qua han chot.</summary>
        public bool ConNhanBai(DateTimeOffset luc)
        {
            return Status == SnapshotStatus.Running && !CollectionFinishedAt.HasValue
                   && (!Deadline.HasValue || luc < Deadline.Value);
        }

        /// <summary>Dong thu bai (mot lan). Tra ve false neu da dong roi.</summary>
        public bool DongThuBai(string lyDo, DateTimeOffset luc)
        {
            if (CollectionFinishedAt.HasValue)
            {
                return false;
            }

            CollectionFinishedAt = luc;
            FinishReason = lyDo;
            return true;
        }

        /// <summary>project.graded: mo han khieu nai. Giao lai → giu moc cu.</summary>
        public bool GhiDaCham(DateTimeOffset luc, TimeSpan hanKhieuNai)
        {
            if (GradedAt.HasValue)
            {
                return false;
            }

            GradedAt = luc;
            AppealDeadline = luc + hanKhieuNai;
            return true;
        }

        /// <summary>Chot so nguoi dat (het han khieu nai, khong con khieu nai mo). Mot lan.</summary>
        public bool Chot(int soNguoiDat, DateTimeOffset luc)
        {
            if (SettledAt.HasValue || !GradedAt.HasValue)
            {
                return false;
            }

            SettledAt = luc;
            PassedCount = soNguoiDat;
            return true;
        }

        public bool Dong(DateTimeOffset occurredAt)
        {
            return DoiTrangThaiNeuMoiHon(SnapshotStatus.Closed, occurredAt);
        }

        /// <summary>Chup tien do cuoi — chi du an DA DONG, va chi mot lan (lan sau giu ban dau).</summary>
        public void ChupTienDoCuoi(string tienDoJson)
        {
            if (string.IsNullOrWhiteSpace(tienDoJson))
            {
                throw new ArgumentException("Thieu tien do.", nameof(tienDoJson));
            }

            if (Status != SnapshotStatus.Closed)
            {
                throw new RuleViolationException("du_an_chua_dong", "Chi don du lieu van hanh cua du an da dong.");
            }

            if (FinalProgressJson == null)
            {
                FinalProgressJson = tienDoJson;
            }
        }

        public void DanhDauDaDon(DateTimeOffset luc)
        {
            if (FinalProgressJson == null)
            {
                throw new InvalidOperationException("Phai chup tien do cuoi truoc khi danh dau da don.");
            }

            if (DataPurgedAt == null)
            {
                DataPurgedAt = luc;
            }
        }

        /// <summary>
        /// gold_set.updated mang TOAN BO tap vang — ban cu den tre khong duoc thay
        /// the ban moi. Tra ve false neu event cu hon (bo qua).
        /// </summary>
        public bool NhanGoldSet(DateTimeOffset occurredAt)
        {
            if (occurredAt < GoldSetAt)
            {
                return false;
            }

            GoldSetAt = occurredAt;
            return true;
        }

        private bool DoiTrangThaiNeuMoiHon(SnapshotStatus moi, DateTimeOffset occurredAt)
        {
            if (Status == SnapshotStatus.Closed)
            {
                return false;
            }

            if (occurredAt < StatusChangedAt)
            {
                return false;
            }

            Status = moi;
            StatusChangedAt = occurredAt;
            return true;
        }
    }
}
