using System;
using System.Collections.Generic;
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
        private List<string> _labelClasses;

        private ProjectSnapshot()
        {
            _labelClasses = new List<string>();
            LabelTaskType = string.Empty;
        }

        public Guid ProjectId { get; private set; }

        public Guid OwnerId { get; private set; }

        public SnapshotStatus Status { get; private set; }

        /// <summary>occurredAt cua event trang thai gan nhat da ap dung.</summary>
        public DateTimeOffset StatusChangedAt { get; private set; }

        /// <summary>false = chua nhan project.published, chua co cau hinh de cap task.</summary>
        public bool IsConfigured { get; private set; }

        /// <summary>
        /// Loai nhan cua du an trong dinh dang nhan chung (Crowd.Labeling), vd
        /// "imageClassification". Rong khi chua publish. Nhan labeler nop duoc
        /// dung theo loai nay — client chi gui phan du lieu.
        /// </summary>
        public string LabelTaskType { get; private set; }

        public IReadOnlyList<string> LabelClasses
        {
            get { return _labelClasses; }
        }

        public bool AllowMultiple { get; private set; }

        public long UnitPriceVnd { get; private set; }

        public int Redundancy { get; private set; }

        public DateTimeOffset? Deadline { get; private set; }

        public bool AllowProfessional { get; private set; }

        public bool IsPrivate { get; private set; }

        public int? MinLevel { get; private set; }

        public int? MinReputation { get; private set; }

        /// <summary>occurredAt cua gold_set.updated gan nhat da ap dung.</summary>
        public DateTimeOffset GoldSetAt { get; private set; }

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
            string labelTaskType,
            IReadOnlyList<string> labelClasses,
            bool allowMultiple,
            long unitPriceVnd,
            int redundancy,
            DateTimeOffset deadline,
            bool allowProfessional,
            bool isPrivate,
            int? minLevel,
            int? minReputation,
            DateTimeOffset occurredAt)
        {
            if (labelClasses == null)
            {
                throw new ArgumentNullException(nameof(labelClasses));
            }

            if (string.IsNullOrWhiteSpace(labelTaskType))
            {
                throw new ArgumentException("Thieu loai nhan.", nameof(labelTaskType));
            }

            OwnerId = ownerId;
            LabelTaskType = labelTaskType;
            _labelClasses = new List<string>(labelClasses);
            AllowMultiple = allowMultiple;
            UnitPriceVnd = unitPriceVnd;
            Redundancy = redundancy;
            Deadline = deadline;
            AllowProfessional = allowProfessional;
            IsPrivate = isPrivate;
            MinLevel = minLevel;
            MinReputation = minReputation;
            IsConfigured = true;

            DoiTrangThaiNeuMoiHon(SnapshotStatus.Running, occurredAt);
        }

        /// <summary>Tra ve false neu event cu hon trang thai hien tai (bi bo qua).</summary>
        public bool TamDung(DateTimeOffset occurredAt)
        {
            return DoiTrangThaiNeuMoiHon(SnapshotStatus.Paused, occurredAt);
        }

        public bool TiepTuc(DateTimeOffset occurredAt)
        {
            return DoiTrangThaiNeuMoiHon(SnapshotStatus.Running, occurredAt);
        }

        public bool Dong(DateTimeOffset occurredAt)
        {
            return DoiTrangThaiNeuMoiHon(SnapshotStatus.Closed, occurredAt);
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

        /// <summary>
        /// Nhan nop len co hop le voi du an khong: dung LOAI nhan cua du an, va
        /// cac lop no dung deu co trong tap nhan. Dinh dang JSON da duoc
        /// LabelPayload kiem truoc do.
        /// </summary>
        public bool LaNhanHopLe(LabelPayload nhan)
        {
            if (nhan == null || nhan.TaskType != LabelTaskType)
            {
                return false;
            }

            return LaBoNhanHopLe(nhan.CacLop());
        }

        /// <summary>
        /// Cung luat voi LabelSchema.LaBoNhanHopLe cua project-svc: khong rong,
        /// moi nhan la lop co that, khong lap, single-label thi dung mot nhan.
        /// </summary>
        public bool LaBoNhanHopLe(IReadOnlyCollection<string> nhan)
        {
            if (nhan == null || nhan.Count == 0)
            {
                return false;
            }

            if (!AllowMultiple && nhan.Count != 1)
            {
                return false;
            }

            HashSet<string> daGap = new HashSet<string>(StringComparer.Ordinal);
            foreach (string n in nhan)
            {
                if (n == null || !_labelClasses.Contains(n) || !daGap.Add(n))
                {
                    return false;
                }
            }

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
