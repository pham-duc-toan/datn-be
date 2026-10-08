using System;

namespace Crowd.Ledger.Domain.Escrows
{
    public enum EscrowState
    {
        Active,

        /// <summary>Da tra phan con lai (huy/hoan thanh). Khong nhan chi tra nua.</summary>
        Closed,
    }

    /// <summary>
    /// Ho so ky quy cua mot du an: ai so huu, redundancy va don gia da chot — de
    /// ledger TU kiem "chi tra co vuot so da ky quy khong" (VD-M-03) ma khong hoi
    /// ai. So TIEN thi khong luu o day: so du tai khoan escrow:project:{id} la su
    /// that duy nhat.
    /// </summary>
    public sealed class ProjectEscrow
    {
        private ProjectEscrow()
        {
        }

        public Guid ProjectId { get; private set; }

        public Guid OwnerId { get; private set; }

        public long ReservedVnd { get; private set; }

        /// <summary>
        /// Tran so lan chi cho MOT task = tran redundancy thich ung cua du an (project.published
        /// MaxRedundancy). 0 = chua biet (chua nhan project.published). Khi da biet thi chan chi tra vuot.
        /// </summary>
        public int Redundancy { get; private set; }

        public EscrowState State { get; private set; }

        public DateTimeOffset ReservedAt { get; private set; }

        public DateTimeOffset? ClosedAt { get; private set; }

        public static ProjectEscrow Tao(Guid projectId, Guid ownerId, long reservedVnd, DateTimeOffset luc)
        {
            ProjectEscrow e = new ProjectEscrow();
            e.ProjectId = projectId;
            e.OwnerId = ownerId;
            e.ReservedVnd = reservedVnd;
            e.State = EscrowState.Active;
            e.ReservedAt = luc;
            return e;
        }

        public void DatRedundancy(int redundancy)
        {
            if (redundancy > 0)
            {
                Redundancy = redundancy;
            }
        }

        /// <summary>Task nay da duoc chi du so luot da ky quy chua.</summary>
        public bool VuotRedundancy(int soLanDaChiChoTask)
        {
            return Redundancy > 0 && soLanDaChiChoTask >= Redundancy;
        }

        public void Dong(DateTimeOffset luc)
        {
            State = EscrowState.Closed;
            ClosedAt = luc;
        }
    }
}
