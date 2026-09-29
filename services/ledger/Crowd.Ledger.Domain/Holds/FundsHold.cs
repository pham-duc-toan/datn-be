using System;
using Crowd.Ledger.Domain.Common;

namespace Crowd.Ledger.Domain.Holds
{
    public enum HoldState
    {
        Held,
        Released,
    }

    /// <summary>
    /// Mot khoan thu lao dang TREO (dac ta 2.11: 3-7 ngay truoc khi rut duoc).
    /// Thoi gian treo cho phep xu tranh chap / gian lan truoc khi tien roi he thong.
    /// </summary>
    public sealed class FundsHold
    {
        private FundsHold()
        {
        }

        public Guid Id { get; private set; }

        /// <summary>UNIQUE: moi nhan duoc duyet sinh DUNG MOT khoan treo.</summary>
        public Guid AnnotationId { get; private set; }

        public Guid LabelerId { get; private set; }

        public Guid ProjectId { get; private set; }

        /// <summary>De dem so luot da chi cho moi task — chan chi vuot redundancy (VD-M-03).</summary>
        public Guid TaskId { get; private set; }

        public long AmountVnd { get; private set; }

        public DateTimeOffset HeldAt { get; private set; }

        public DateTimeOffset ReleaseAt { get; private set; }

        public HoldState State { get; private set; }

        public DateTimeOffset? ReleasedAt { get; private set; }

        public static FundsHold Tao(Guid annotationId, Guid projectId, Guid taskId, Guid labelerId, long amount, DateTimeOffset luc, TimeSpan thoiGianTreo)
        {
            FundsHold h = new FundsHold();
            h.Id = Guid.CreateVersion7();
            h.AnnotationId = annotationId;
            h.LabelerId = labelerId;
            h.ProjectId = projectId;
            h.TaskId = taskId;
            h.AmountVnd = amount;
            h.HeldAt = luc;
            h.ReleaseAt = luc + thoiGianTreo;
            h.State = HoldState.Held;
            return h;
        }

        public bool DenHan(DateTimeOffset luc)
        {
            return State == HoldState.Held && luc >= ReleaseAt;
        }

        public void GiaiPhong(DateTimeOffset luc)
        {
            if (!DenHan(luc))
            {
                throw new RuleViolationException("chua_den_han", "Khoan treo chua den han hoac da giai phong.");
            }

            State = HoldState.Released;
            ReleasedAt = luc;
        }
    }
}
