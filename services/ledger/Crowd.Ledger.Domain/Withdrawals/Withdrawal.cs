using System;
using Crowd.Ledger.Domain.Common;

namespace Crowd.Ledger.Domain.Withdrawals
{
    public enum WithdrawalState
    {
        /// <summary>Da duyet, da gui payment-svc — cho cong chuyen khoan.</summary>
        Requested,
        Completed,

        /// <summary>Cong tu choi — da dao but toan, tien ve lai labeler.</summary>
        Failed,

        /// <summary>
        /// Cho admin duyet. Tien DA giu (tru kha dung, nam o "dang chuyen") nen
        /// labeler khong rut trung duoc, nhung CHUA gui payment-svc.
        /// </summary>
        PendingApproval,

        /// <summary>Admin tu choi — da dao but toan, tien ve lai labeler.</summary>
        Rejected,
    }

    /// <summary>Luat rut tien DANG HIEU LUC — tang Api doc tu setting ledger.withdraw_*.</summary>
    public sealed class QuyDinhRut
    {
        /// <summary>ledger.withdraw_min_vnd — tranh phi chuyen khoan an het tien nho.</summary>
        public required long ToiThieuVnd { get; init; }

        /// <summary>ledger.withdraw_tax_threshold_vnd — nguong khau tru thue TNCN tren MOT lan chi.</summary>
        public required long NguongThueVnd { get; init; }

        /// <summary>ledger.withdraw_tax_percent.</summary>
        public required int ThueSuatPhanTram { get; init; }
    }

    /// <summary>Mot lenh rut tien cua labeler (FL-11).</summary>
    public sealed class Withdrawal
    {
        /// <summary>Do rong cot idempotency_key.</summary>
        public const int DoDaiKhoaToiDa = 100;

        /// <summary>Do rong cot failure_reason (ly do that bai / tu choi).</summary>
        public const int DoDaiLyDoToiDa = 500;

        private Withdrawal()
        {
            IdempotencyKey = string.Empty;
            BankAccount = string.Empty;
        }

        public Guid Id { get; private set; }

        public Guid LabelerId { get; private set; }

        /// <summary>So tien tru khoi so du.</summary>
        public long AmountVnd { get; private set; }

        public long TaxVnd { get; private set; }

        /// <summary>So tien thuc chuyen = Amount − Tax.</summary>
        public long NetVnd { get; private set; }

        public string BankAccount { get; private set; }

        /// <summary>
        /// Header Idempotency-Key cua client. UNIQUE (labeler, key): bam "rut" hai
        /// lan vi mang cham chi tao MOT lenh (docs 3.3).
        /// </summary>
        public string IdempotencyKey { get; private set; }

        public WithdrawalState State { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public DateTimeOffset? CompletedAt { get; private set; }

        public string? FailureReason { get; private set; }

        /// <summary>Admin duyet / tu choi. null khi he thong tu duyet (setting ledger.withdraw_auto_approve).</summary>
        public Guid? ReviewedBy { get; private set; }

        public DateTimeOffset? ReviewedAt { get; private set; }

        /// <summary>
        /// Thue TNCN: thue suat khi MOT lan chi tu nguong tro len, lam tron XUONG
        /// den dong (VD-M-07). Duoi nguong thi 0. Nguong xet THEO TUNG LAN chi.
        /// </summary>
        public static long TinhThue(long amount, long nguongThueVnd, int thueSuatPhanTram)
        {
            if (amount < nguongThueVnd)
            {
                return 0;
            }

            return checked(amount * thueSuatPhanTram) / 100;
        }

        /// <summary>Lenh moi o trang thai CHO DUYET. Service goi Duyet ngay neu duoc tu duyet.</summary>
        public static Withdrawal Tao(
            Guid labelerId, long amount, string bankAccount, string idempotencyKey, QuyDinhRut quyDinh, DateTimeOffset luc)
        {
            if (quyDinh == null)
            {
                throw new ArgumentNullException(nameof(quyDinh));
            }

            long toiThieuVnd = quyDinh.ToiThieuVnd;
            if (amount <= 0)
            {
                throw new InvalidValueException("so_tien_khong_hop_le", "So tien rut phai lon hon 0.");
            }

            if (amount < toiThieuVnd)
            {
                throw new InvalidValueException("duoi_muc_toi_thieu", "So tien rut toi thieu la " + toiThieuVnd + "d.");
            }

            string tk = bankAccount == null ? string.Empty : bankAccount.Trim();
            if (tk.Length == 0 || tk.Length > 100)
            {
                throw new InvalidValueException("thieu_tai_khoan", "Can so tai khoan ngan hang nhan tien.");
            }

            if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > DoDaiKhoaToiDa)
            {
                throw new InvalidValueException("thieu_idempotency_key", "Can header Idempotency-Key (1-100 ky tu).");
            }

            Withdrawal w = new Withdrawal();
            w.Id = Guid.CreateVersion7();
            w.LabelerId = labelerId;
            w.AmountVnd = amount;
            w.TaxVnd = TinhThue(amount, quyDinh.NguongThueVnd, quyDinh.ThueSuatPhanTram);
            w.NetVnd = amount - w.TaxVnd;
            w.BankAccount = tk;
            w.IdempotencyKey = idempotencyKey;
            w.State = WithdrawalState.PendingApproval;
            w.CreatedAt = luc;
            return w;
        }

        /// <summary>Duyet lenh: chuyen sang Requested (service phat payout.requested). nguoiDuyet null = he thong tu duyet.</summary>
        public void Duyet(Guid? nguoiDuyet, DateTimeOffset luc)
        {
            ChiKhiChoDuyet();
            State = WithdrawalState.Requested;
            ReviewedBy = nguoiDuyet;
            ReviewedAt = luc;
        }

        /// <summary>Admin tu choi: service ghi but toan dao, tien ve lai labeler.</summary>
        public void TuChoi(Guid nguoiDuyet, string lyDo, DateTimeOffset luc)
        {
            ChiKhiChoDuyet();
            string ly = lyDo == null ? string.Empty : lyDo.Trim();
            if (ly.Length == 0 || ly.Length > DoDaiLyDoToiDa)
            {
                throw new InvalidValueException("thieu_ly_do", "Tu choi lenh rut phai ghi ly do (1-" + DoDaiLyDoToiDa + " ky tu).");
            }

            State = WithdrawalState.Rejected;
            ReviewedBy = nguoiDuyet;
            ReviewedAt = luc;
            CompletedAt = luc;
            FailureReason = ly;
        }

        private void ChiKhiChoDuyet()
        {
            if (State != WithdrawalState.PendingApproval)
            {
                throw new RuleViolationException("lenh_rut_khong_cho_duyet", "Lenh rut khong o trang thai cho duyet (" + State + ").");
            }
        }

        public void HoanTat(DateTimeOffset luc)
        {
            ChiKhiDangCho();
            State = WithdrawalState.Completed;
            CompletedAt = luc;
        }

        public void ThatBai(string lyDo, DateTimeOffset luc)
        {
            ChiKhiDangCho();
            State = WithdrawalState.Failed;
            CompletedAt = luc;
            FailureReason = lyDo;
        }

        private void ChiKhiDangCho()
        {
            if (State != WithdrawalState.Requested)
            {
                throw new RuleViolationException("lenh_rut_da_chot", "Lenh rut da chot (" + State + ").");
            }
        }
    }
}
