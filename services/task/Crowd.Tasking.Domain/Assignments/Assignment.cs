using System;
using System.Collections.Generic;
using Crowd.Tasking.Domain.Common;
using Crowd.Tasking.Domain.Tasks;

namespace Crowd.Tasking.Domain.Assignments
{
    public enum AssignmentState
    {
        /// <summary>Dang giu (lease), cho nop.</summary>
        Leased,

        Submitted,

        /// <summary>Labeler tu bam "bo qua" (FL-05).</summary>
        Released,

        /// <summary>Qua 15 phut khong nop — reaper thu lai.</summary>
        Expired,

        /// <summary>He thong thu hoi: labeler bi chan/khoa, du an dong.</summary>
        Revoked,
    }

    /// <summary>
    /// Mot LUOT LEASE (FL-06): labeler X giu task T trong 15 phut.
    ///
    /// Luat chong nop tre (VD-T-01): Nop() chi thanh cong khi luot nay CON
    /// Leased va CHUA qua ExpiresAt. Reaper chuyen Leased → Expired thi Nop()
    /// that bai — va service khoa DONG khi goi Nop() nen reaper va nop khong the
    /// cung thang (xem LeaseService).
    ///
    /// Postgres la su that DUY NHAT ve lease. docs 3.5 de xuat them Redis giu
    /// khoa; o day co y bo: cau UPDATE co dieu kien tren dong da khoa da nguyen
    /// tu san, them Redis chi tao HAI nguon su that phai dong bo (VD-T-02 chinh
    /// la loi do hai nguon lech nhau).
    /// </summary>
    public sealed class Assignment
    {
        private List<string> _labels;

        private Assignment()
        {
            _labels = new List<string>();
        }

        public Guid Id { get; private set; }

        public Guid TaskId { get; private set; }

        public Guid ProjectId { get; private set; }

        public Guid SampleId { get; private set; }

        public Guid LabelerId { get; private set; }

        public AssignmentState State { get; private set; }

        public DateTimeOffset LeasedAt { get; private set; }

        public DateTimeOffset ExpiresAt { get; private set; }

        /// <summary>Luc ket thuc (nop, bo qua, het han, thu hoi).</summary>
        public DateTimeOffset? EndedAt { get; private set; }

        /// <summary>Nhan da nop. Rong khi chua nop.</summary>
        public IReadOnlyList<string> Labels
        {
            get { return _labels; }
        }

        public static Assignment Tao(LabelingTask task, Guid labelerId, DateTimeOffset luc, TimeSpan thoiHan)
        {
            if (task == null)
            {
                throw new ArgumentNullException(nameof(task));
            }

            // Tang bo dem truoc: task het cho thi nem loi, khong tao luot lease thua.
            task.GhiNhanLease();

            Assignment a = new Assignment();
            a.Id = Guid.CreateVersion7();
            a.TaskId = task.Id;
            a.ProjectId = task.ProjectId;
            a.SampleId = task.SampleId;
            a.LabelerId = labelerId;
            a.State = AssignmentState.Leased;
            a.LeasedAt = luc;
            a.ExpiresAt = luc + thoiHan;
            return a;
        }

        public bool DangGiu(DateTimeOffset luc)
        {
            return State == AssignmentState.Leased && luc < ExpiresAt;
        }

        /// <summary>Nop nhan. Tra ve true neu task vua du redundancy.</summary>
        public bool Nop(LabelingTask task, IReadOnlyList<string> nhan, DateTimeOffset luc)
        {
            if (task == null)
            {
                throw new ArgumentNullException(nameof(task));
            }

            if (nhan == null)
            {
                throw new ArgumentNullException(nameof(nhan));
            }

            if (State != AssignmentState.Leased)
            {
                throw new RuleViolationException(
                    "lease_khong_con",
                    "Luot nhan task nay da ket thuc (" + State + "), khong nop duoc nua.");
            }

            if (luc >= ExpiresAt)
            {
                throw new RuleViolationException(
                    "lease_het_han",
                    "Da qua thoi han giu task. Task co the da chuyen sang nguoi khac.");
            }

            State = AssignmentState.Submitted;
            EndedAt = luc;
            _labels = new List<string>(nhan);

            return task.GhiNhanNop(luc);
        }

        /// <summary>Labeler bo qua task (FL-05).</summary>
        public void BoQua(LabelingTask task, DateTimeOffset luc)
        {
            KetThucKhongNop(task, AssignmentState.Released, luc);
        }

        /// <summary>Reaper: da qua han.</summary>
        public void HetHan(LabelingTask task, DateTimeOffset luc)
        {
            if (luc < ExpiresAt)
            {
                throw new RuleViolationException("chua_het_han", "Lease chua het han.");
            }

            KetThucKhongNop(task, AssignmentState.Expired, luc);
        }

        /// <summary>He thong thu hoi: labeler bi chan/khoa, hoac du an dong.</summary>
        public void ThuHoi(LabelingTask task, DateTimeOffset luc)
        {
            KetThucKhongNop(task, AssignmentState.Revoked, luc);
        }

        private void KetThucKhongNop(LabelingTask task, AssignmentState trangThaiMoi, DateTimeOffset luc)
        {
            if (task == null)
            {
                throw new ArgumentNullException(nameof(task));
            }

            if (State != AssignmentState.Leased)
            {
                throw new RuleViolationException(
                    "lease_khong_con",
                    "Luot nhan task nay da ket thuc (" + State + ").");
            }

            State = trangThaiMoi;
            EndedAt = luc;
            task.TraLease();
        }
    }
}
