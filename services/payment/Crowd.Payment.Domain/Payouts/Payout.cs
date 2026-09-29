using System;
using Crowd.Payment.Domain.Common;

namespace Crowd.Payment.Domain.Payouts
{
    public enum PayoutStatus
    {
        /// <summary>PHA 1: da ghi y dinh chuyen tien, chua goi cong.</summary>
        Requested,

        /// <summary>PHA 2: dang goi / da goi cong nhung CHUA BIET ket qua.</summary>
        Sending,

        Succeeded,
        Failed,
    }

    /// <summary>
    /// Mot lenh chuyen tien ra ngoai cho labeler. Id = WithdrawalId cua ledger.
    ///
    /// BA PHA (VD-M-09) — KHONG BAO GIO goi cong ben trong transaction database:
    ///   1. Requested → Sending   : ghi y dinh, COMMIT
    ///   2. goi cong               : khong transaction nao mo
    ///   3. Sending → ket qua      : transaction MOI ghi ket qua + event
    /// Chet giua pha 2 va 3 → lenh ket o Sending. Worker tra cuu nguoc cong theo
    /// Id (cong nhan Id nay lam khoa idempotency nen goi lai khong chuyen hai lan).
    /// </summary>
    public sealed class Payout
    {
        private Payout()
        {
            BankAccount = string.Empty;
        }

        public Guid Id { get; private set; }

        public Guid LabelerId { get; private set; }

        public long NetVnd { get; private set; }

        public string BankAccount { get; private set; }

        public PayoutStatus Status { get; private set; }

        public int Attempts { get; private set; }

        public string? ProviderTxnId { get; private set; }

        public string? LastError { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public DateTimeOffset? SentAt { get; private set; }

        public DateTimeOffset? CompletedAt { get; private set; }

        public static Payout Tao(Guid withdrawalId, Guid labelerId, long netVnd, string bankAccount, DateTimeOffset luc)
        {
            if (netVnd <= 0)
            {
                throw new InvalidValueException("so_tien_khong_hop_le", "So tien chuyen phai lon hon 0.");
            }

            Payout p = new Payout();
            p.Id = withdrawalId;
            p.LabelerId = labelerId;
            p.NetVnd = netVnd;
            p.BankAccount = bankAccount;
            p.Status = PayoutStatus.Requested;
            p.CreatedAt = luc;
            return p;
        }

        public void BatDauGui(DateTimeOffset luc)
        {
            if (Status != PayoutStatus.Requested)
            {
                throw new RuleViolationException("khong_the_gui", "Lenh chuyen dang o trang thai " + Status + ".");
            }

            Status = PayoutStatus.Sending;
            Attempts = Attempts + 1;
            SentAt = luc;
        }

        /// <summary>Ket qua CHUA RO (timeout mang): giu Sending, de worker tra cuu lai sau.</summary>
        public void GhiLoiTam(string loi, DateTimeOffset luc)
        {
            LastError = loi;
            SentAt = luc;
        }

        public void ThanhCong(string providerTxnId, DateTimeOffset luc)
        {
            ChiKhiDangGui();
            Status = PayoutStatus.Succeeded;
            ProviderTxnId = providerTxnId;
            CompletedAt = luc;
        }

        public void ThatBai(string lyDo, DateTimeOffset luc)
        {
            ChiKhiDangGui();
            Status = PayoutStatus.Failed;
            LastError = lyDo;
            CompletedAt = luc;
        }

        /// <summary>Ket o Sending qua lau → can tra cuu nguoc cong (VD-M-09).</summary>
        public bool CanTraCuu(TimeSpan quaLau, DateTimeOffset luc)
        {
            return Status == PayoutStatus.Sending && SentAt.HasValue && luc - SentAt.Value >= quaLau;
        }

        private void ChiKhiDangGui()
        {
            if (Status != PayoutStatus.Sending)
            {
                throw new RuleViolationException("khong_dang_gui", "Lenh chuyen khong o trang thai dang gui (" + Status + ").");
            }
        }
    }
}
