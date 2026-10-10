using System;

namespace Crowd.Ledger.Domain.Escrows
{
    public enum EscrowState
    {
        Active,

        /// <summary>
        /// Du an da ket thuc nhung CON nhan da duyet chua chi (annotation.approved toi sau
        /// project.completed — hai queue khac nhau, NC-B-01). Van nhan chi tra cho cac nhan do;
        /// chi DU thi tra phan con lai va chuyen Closed. Khong chi cho cong link nua.
        /// </summary>
        Closing,

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

        // ---- Dieu khoan da chot luc publish (project.published) — de tinh ngan sach CONG LINK ----

        public int SampleCount { get; private set; }

        public long UnitPriceVnd { get; private set; }

        public long PlatformFeeVnd { get; private set; }

        public bool AllowLinkGateway { get; private set; }

        /// <summary>Tong ky quy da tieu cho kenh cong link (sharer + phan nen tang).</summary>
        public long GateSpentVnd { get; private set; }

        /// <summary>So thu tu tang dan cua gate.budget_changed — gate bo qua event cu den tre.</summary>
        public long GateBudgetSequence { get; private set; }

        public DateTimeOffset ReservedAt { get; private set; }

        public DateTimeOffset? ClosedAt { get; private set; }

        /// <summary>Closing: so nhan da duyet luc dong so — chi du chung thi moi hoan ky quy.</summary>
        public int? ExpectedPaidAnnotations { get; private set; }

        /// <summary>Closing: dong vi huy (refund.issued) hay hoan thanh (escrow.released).</summary>
        public bool ClosingIsCancel { get; private set; }

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

        /// <summary>project.published: chot so mau, don gia, phi, kenh cong link.</summary>
        public void GhiDieuKhoan(int sampleCount, long unitPriceVnd, long platformFeeVnd, bool allowLinkGateway)
        {
            SampleCount = sampleCount;
            UnitPriceVnd = unitPriceVnd;
            PlatformFeeVnd = platformFeeVnd;
            AllowLinkGateway = allowLinkGateway;
        }

        /// <summary>
        /// Phan ky quy DANH RIENG cho labeler chuyen nghiep: so mau x tran redundancy x
        /// (don gia + phi) — dung muc toi thieu project-svc bat ky quy luc publish.
        /// </summary>
        public long DanhChoChuyenNghiepVnd()
        {
            return checked((long)SampleCount * Redundancy * (UnitPriceVnd + PlatformFeeVnd));
        }

        /// <summary>
        /// Ngan sach CONG LINK con lai = ky quy − phan danh cho chuyen nghiep − da tieu cho
        /// cong link. Cong link KHONG BAO GIO an vao tien da danh cho labeler (VD-L-03):
        /// doanh nghiep muon chay cong link thi dat ngan sach cao hon muc toi thieu.
        /// </summary>
        public long NganSachCongConLai()
        {
            if (!AllowLinkGateway || State != EscrowState.Active || SampleCount <= 0 || Redundancy <= 0)
            {
                return 0;
            }

            return Math.Max(0, ReservedVnd - DanhChoChuyenNghiepVnd() - GateSpentVnd);
        }

        public void TieuChoCongLink(long soTien)
        {
            if (soTien <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(soTien), "So tien phai duong.");
            }

            GateSpentVnd = checked(GateSpentVnd + soTien);
        }

        public long SoThuTuNganSachMoi()
        {
            GateBudgetSequence = GateBudgetSequence + 1;
            return GateBudgetSequence;
        }

        /// <summary>Task nay da duoc chi du so luot da ky quy chua.</summary>
        public bool VuotRedundancy(int soLanDaChiChoTask)
        {
            return Redundancy > 0 && soLanDaChiChoTask >= Redundancy;
        }

        /// <summary>Du an ket thuc ma con nhan da duyet chua chi: giu ky quy cho toi khi chi du.</summary>
        public void BatDauDong(int soNhanCanChi, bool laHuy)
        {
            if (State != EscrowState.Active)
            {
                throw new InvalidOperationException("Chi bat dau dong duoc ky quy dang Active (dang " + State + ").");
            }

            if (soNhanCanChi <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(soNhanCanChi), "So nhan can chi phai duong.");
            }

            State = EscrowState.Closing;
            ExpectedPaidAnnotations = soNhanCanChi;
            ClosingIsCancel = laHuy;
        }

        /// <summary>Dang dong va da chi du so nhan da duyet luc dong so.</summary>
        public bool DaChiDuDeDong(int soNhanDaChi)
        {
            return State == EscrowState.Closing
                   && ExpectedPaidAnnotations.HasValue
                   && soNhanDaChi >= ExpectedPaidAnnotations.Value;
        }

        public void Dong(DateTimeOffset luc)
        {
            State = EscrowState.Closed;
            ClosedAt = luc;
        }
    }
}
