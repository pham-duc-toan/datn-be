using System;

namespace Crowd.Ledger.Domain.Escrows
{
    public enum EscrowState
    {
        Active,

        /// <summary>
        /// Du an da ket thuc nhung CON bai dat chua tra thuong (project.graded / khieu nai chap nhan toi
        /// sau project.completed — cac queue khac nhau, NC-B-01). Van tra thuong cho cac bai do; tra DU
        /// thi hoan phan con lai va chuyen Closed. Khong chi cho cong link nua.
        /// </summary>
        Closing,

        /// <summary>Da tra phan con lai (huy/hoan thanh). Khong nhan chi tra nua.</summary>
        Closed,
    }

    /// <summary>
    /// Ho so ky quy cua mot du an (docs 3.18 — du an lam trung tam): ai so huu, so suat, tien thuong moi
    /// suat va phi da chot luc publish — de ledger TU kiem "tra thuong co vuot so da ky quy khong" (VD-M-03)
    /// ma khong hoi ai. So TIEN thi khong luu o day: so du tai khoan escrow:project:{id} la su that duy nhat.
    ///
    /// Ky quy = so suat x (thuong + phi) + ngan sach cong link.
    /// </summary>
    public sealed class ProjectEscrow
    {
        private ProjectEscrow()
        {
        }

        public Guid ProjectId { get; private set; }

        public Guid OwnerId { get; private set; }

        public long ReservedVnd { get; private set; }

        /// <summary>So suat (toi da bai dat duoc thuong). 0 = chua biet (chua nhan project.published).</summary>
        public int Slots { get; private set; }

        public EscrowState State { get; private set; }

        // ---- Dieu khoan da chot luc publish (project.published v2) ----

        /// <summary>Tien thuong cho MOT bai dat.</summary>
        public long RewardVnd { get; private set; }

        /// <summary>Phi nen tang tren MOT bai dat.</summary>
        public long PlatformFeeVnd { get; private set; }

        public bool AllowLinkGateway { get; private set; }

        /// <summary>Ngan sach danh rieng cho cong link (0 neu khong bat).</summary>
        public long GateBudgetVnd { get; private set; }

        /// <summary>Tong ky quy da tieu cho kenh cong link (sharer + phan nen tang).</summary>
        public long GateSpentVnd { get; private set; }

        /// <summary>So thu tu tang dan cua gate.budget_changed — gate bo qua event cu den tre.</summary>
        public long GateBudgetSequence { get; private set; }

        public DateTimeOffset ReservedAt { get; private set; }

        /// <summary>Ten du an (in len hoa don VAT, FB-05); null voi du an publish truoc khi co truong nay.</summary>
        public string? Name { get; private set; }

        public DateTimeOffset? ClosedAt { get; private set; }

        /// <summary>Closing: so bai dat cuoi cung (project.completed) — tra du chung thi moi hoan ky quy.</summary>
        public int? ExpectedPaidAttempts { get; private set; }

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

        public void DatTen(string? ten)
        {
            if (ten == null)
            {
                return;
            }

            string sach = ten.Trim();
            Name = sach.Length > 200 ? sach.Substring(0, 200) : sach;
        }

        /// <summary>project.published v2: chot so suat, thuong, phi, kenh cong link.</summary>
        public void GhiDieuKhoan(int slots, long rewardVnd, long platformFeeVnd, bool allowLinkGateway, long gateBudgetVnd)
        {
            if (slots > 0)
            {
                Slots = slots;
            }

            RewardVnd = rewardVnd;
            PlatformFeeVnd = platformFeeVnd;
            AllowLinkGateway = allowLinkGateway;
            GateBudgetVnd = Math.Max(0, gateBudgetVnd);
        }

        /// <summary>Da chot dieu khoan thuong chua (project.published da toi).</summary>
        public bool DaCoDieuKhoan
        {
            get { return Slots > 0 && RewardVnd > 0; }
        }

        /// <summary>Phan ky quy DANH RIENG cho labeler: so suat x (thuong + phi).</summary>
        public long DanhChoLabelerVnd()
        {
            return checked((long)Slots * (RewardVnd + PlatformFeeVnd));
        }

        /// <summary>
        /// Ngan sach CONG LINK con lai = ngan sach cong link − da tieu, va KHONG BAO GIO an vao phan danh
        /// cho labeler (VD-L-03).
        /// </summary>
        public long NganSachCongConLai()
        {
            if (!AllowLinkGateway || State != EscrowState.Active || Slots <= 0)
            {
                return 0;
            }

            long theoNganSach = GateBudgetVnd - GateSpentVnd;
            long theoKyQuy = ReservedVnd - DanhChoLabelerVnd() - GateSpentVnd;
            return Math.Max(0, Math.Min(theoNganSach, theoKyQuy));
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

        /// <summary>Da tra thuong du so suat chua (chan tra vuot ky quy, VD-M-03).</summary>
        public bool VuotSoSuat(int soBaiDaTra)
        {
            return Slots > 0 && soBaiDaTra >= Slots;
        }

        /// <summary>Du an ket thuc ma con bai dat chua tra thuong: giu ky quy cho toi khi tra du.</summary>
        public void BatDauDong(int soBaiCanTra, bool laHuy)
        {
            if (State != EscrowState.Active)
            {
                throw new InvalidOperationException("Chi bat dau dong duoc ky quy dang Active (dang " + State + ").");
            }

            if (soBaiCanTra <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(soBaiCanTra), "So bai can tra phai duong.");
            }

            State = EscrowState.Closing;
            ExpectedPaidAttempts = soBaiCanTra;
            ClosingIsCancel = laHuy;
        }

        /// <summary>Dang dong va da tra du so bai dat cuoi cung.</summary>
        public bool DaChiDuDeDong(int soBaiDaTra)
        {
            return State == EscrowState.Closing
                   && ExpectedPaidAttempts.HasValue
                   && soBaiDaTra >= ExpectedPaidAttempts.Value;
        }

        public void Dong(DateTimeOffset luc)
        {
            State = EscrowState.Closed;
            ClosedAt = luc;
        }
    }
}
