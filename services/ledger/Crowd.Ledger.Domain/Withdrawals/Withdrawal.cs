using System;
using Crowd.Ledger.Domain.Common;

namespace Crowd.Ledger.Domain.Withdrawals
{
    public enum WithdrawalState
    {
        /// <summary>Da tru so du, cho cong chuyen khoan.</summary>
        Requested,
        Completed,

        /// <summary>Cong tu choi — da dao but toan, tien ve lai labeler.</summary>
        Failed,
    }

    /// <summary>Mot lenh rut tien cua labeler (FL-11).</summary>
    public sealed class Withdrawal
    {
        /// <summary>Nguong khau tru thue TNCN tren MOT lan chi (dac ta 2.11, VD-M-11).</summary>
        public const long NguongThueVnd = 2000000;

        public const int ThueSuatPhanTram = 10;

        public const int DoDaiKhoaToiDa = 100;

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

        /// <summary>
        /// Thue TNCN: 10% khi MOT lan chi tu 2.000.000d tro len, lam tron XUONG
        /// den dong (VD-M-07). Duoi nguong thi 0. Nguong xet THEO TUNG LAN chi.
        /// </summary>
        public static long TinhThue(long amount)
        {
            if (amount < NguongThueVnd)
            {
                return 0;
            }

            return checked(amount * ThueSuatPhanTram) / 100;
        }

        public static Withdrawal Tao(
            Guid labelerId, long amount, string bankAccount, string idempotencyKey, long toiThieuVnd, DateTimeOffset luc)
        {
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
            w.TaxVnd = TinhThue(amount);
            w.NetVnd = amount - w.TaxVnd;
            w.BankAccount = tk;
            w.IdempotencyKey = idempotencyKey;
            w.State = WithdrawalState.Requested;
            w.CreatedAt = luc;
            return w;
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
