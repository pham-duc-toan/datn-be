using System;
using Crowd.Payment.Domain.Common;

namespace Crowd.Payment.Domain.Deposits
{
    public enum PaymentIntentStatus
    {
        /// <summary>Da tao link thanh toan, cho cong bao ket qua.</summary>
        Pending,
        Succeeded,
        Failed,
    }

    /// <summary>
    /// Mot lan doanh nghiep nap tien (FB-03). Chi chuyen sang Succeeded khi cong
    /// goi webhook CO CHU KY HOP LE — khong bao gio vi client noi "toi da tra roi".
    /// </summary>
    public sealed class PaymentIntent
    {
        public const long NapToiThieuVnd = 10000;
        public const long NapToiDaVnd = 500000000;

        private PaymentIntent()
        {
            Provider = string.Empty;
            IdempotencyKey = string.Empty;
        }

        public Guid Id { get; private set; }

        public Guid BusinessId { get; private set; }

        public long AmountVnd { get; private set; }

        public string Provider { get; private set; }

        /// <summary>UNIQUE (business, key): bam "nap" hai lan chi tao MOT lan nap.</summary>
        public string IdempotencyKey { get; private set; }

        public PaymentIntentStatus Status { get; private set; }

        /// <summary>
        /// Ma giao dich phia cong. UNIQUE (provider, provider_txn_id): webhook den 3
        /// lan cung chi ghi MOT lan (docs 3.3).
        /// </summary>
        public string? ProviderTxnId { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public DateTimeOffset? CompletedAt { get; private set; }

        public static PaymentIntent Tao(Guid businessId, long amount, string provider, string idempotencyKey, DateTimeOffset luc)
        {
            // VD-M-05: so tien nguoi dung xin luon duong.
            if (amount < NapToiThieuVnd || amount > NapToiDaVnd)
            {
                throw new InvalidValueException(
                    "so_tien_nap_khong_hop_le",
                    "So tien nap phai tu " + NapToiThieuVnd + " den " + NapToiDaVnd + "d.");
            }

            if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100)
            {
                throw new InvalidValueException("thieu_idempotency_key", "Can header Idempotency-Key (1-100 ky tu).");
            }

            PaymentIntent p = new PaymentIntent();
            p.Id = Guid.CreateVersion7();
            p.BusinessId = businessId;
            p.AmountVnd = amount;
            p.Provider = provider;
            p.IdempotencyKey = idempotencyKey;
            p.Status = PaymentIntentStatus.Pending;
            p.CreatedAt = luc;
            return p;
        }

        /// <summary>
        /// Cong bao da thu tien. Tra ve false neu webhook nay la BAN LAP cua lan da
        /// ghi (cung ma giao dich) — khong phai loi, chi khong lam gi them.
        ///
        /// So tien cong bao PHAI KHOP so tien da tao: ke tan cong sua tham so tren
        /// trang thanh toan de tra 10.000d cho lenh nap 10.000.000d se bi chan o day.
        /// </summary>
        public bool XacNhanThanhCong(string providerTxnId, long soTienCongBao, DateTimeOffset luc)
        {
            if (string.IsNullOrWhiteSpace(providerTxnId))
            {
                throw new InvalidValueException("thieu_ma_giao_dich", "Webhook thieu ma giao dich cua cong.");
            }

            if (Status == PaymentIntentStatus.Succeeded)
            {
                if (string.Equals(ProviderTxnId, providerTxnId, StringComparison.Ordinal))
                {
                    return false;
                }

                throw new RuleViolationException("da_thanh_toan", "Lenh nap da thanh cong voi ma giao dich khac.");
            }

            if (Status != PaymentIntentStatus.Pending)
            {
                throw new RuleViolationException("lenh_nap_da_chot", "Lenh nap da chot (" + Status + ").");
            }

            if (soTienCongBao != AmountVnd)
            {
                throw new RuleViolationException(
                    "sai_so_tien",
                    "Cong bao " + soTienCongBao + "d nhung lenh nap la " + AmountVnd + "d.");
            }

            Status = PaymentIntentStatus.Succeeded;
            ProviderTxnId = providerTxnId;
            CompletedAt = luc;
            return true;
        }

        public void ThatBai(DateTimeOffset luc)
        {
            if (Status == PaymentIntentStatus.Pending)
            {
                Status = PaymentIntentStatus.Failed;
                CompletedAt = luc;
            }
        }
    }
}
