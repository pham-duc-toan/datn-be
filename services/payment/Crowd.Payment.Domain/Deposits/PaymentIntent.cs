using System;
using Crowd.Payment.Domain.Common;

namespace Crowd.Payment.Domain.Deposits
{
    public enum PaymentIntentStatus
    {
        /// <summary>Da tao link thanh toan (hoac thong tin chuyen khoan), cho tien ve.</summary>
        Pending,
        Succeeded,
        Failed,

        /// <summary>Chuyen khoan thu cong: doanh nghiep bao "da chuyen", cho admin doi chieu sao ke.</summary>
        AwaitingApproval,

        /// <summary>Chuyen khoan thu cong: admin khong thay tien ve / sai noi dung.</summary>
        Rejected,
    }

    /// <summary>Han muc nap DANG HIEU LUC — tang Api doc tu setting payment.deposit_*.</summary>
    public sealed class QuyDinhNap
    {
        public required long ToiThieuVnd { get; init; }

        public required long ToiDaVnd { get; init; }
    }

    /// <summary>
    /// Mot lan doanh nghiep nap tien (FB-03). Chi chuyen sang Succeeded khi cong
    /// goi webhook CO CHU KY HOP LE — khong bao gio vi client noi "toi da tra roi".
    /// </summary>
    public sealed class PaymentIntent
    {
        /// <summary>Ten "cong" cua lenh nap chuyen khoan ngan hang thu cong (admin doi chieu).</summary>
        public const string ChuyenKhoanThuCong = "manual_transfer";

        /// <summary>Do rong cot reject_reason.</summary>
        public const int DoDaiLyDoToiDa = 500;

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

        /// <summary>
        /// Chuyen khoan thu cong: noi dung doanh nghiep PHAI ghi khi chuyen khoan, de
        /// admin tim dung lenh trong sao ke. null voi cong thanh toan.
        /// </summary>
        public string? TransferCode { get; private set; }

        /// <summary>Luc doanh nghiep bao "da chuyen".</summary>
        public DateTimeOffset? TransferredAt { get; private set; }

        /// <summary>Admin duyet / tu choi. null khi he thong tu duyet theo nguong.</summary>
        public Guid? ReviewedBy { get; private set; }

        public string? RejectReason { get; private set; }

        public bool LaChuyenKhoanThuCong
        {
            get { return string.Equals(Provider, ChuyenKhoanThuCong, StringComparison.Ordinal); }
        }

        public static PaymentIntent Tao(
            Guid businessId, long amount, string provider, string idempotencyKey, QuyDinhNap quyDinh, DateTimeOffset luc)
        {
            if (quyDinh == null)
            {
                throw new ArgumentNullException(nameof(quyDinh));
            }

            // VD-M-05: so tien nguoi dung xin luon duong.
            if (amount <= 0 || amount < quyDinh.ToiThieuVnd || amount > quyDinh.ToiDaVnd)
            {
                throw new InvalidValueException(
                    "so_tien_nap_khong_hop_le",
                    "So tien nap phai tu " + quyDinh.ToiThieuVnd + " den " + quyDinh.ToiDaVnd + "d.");
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

        /// <summary>Lenh nap chuyen khoan thu cong — kem ma noi dung chuyen khoan duy nhat.</summary>
        public static PaymentIntent TaoChuyenKhoan(Guid businessId, long amount, string idempotencyKey, QuyDinhNap quyDinh, DateTimeOffset luc)
        {
            PaymentIntent p = Tao(businessId, amount, ChuyenKhoanThuCong, idempotencyKey, quyDinh, luc);
            p.TransferCode = "CROWD" + p.Id.ToString("N").Substring(20).ToUpperInvariant();
            return p;
        }

        /// <summary>Doanh nghiep bao da chuyen khoan → cho admin doi chieu. Bam lai khi da bao thi khong lam gi.</summary>
        public void BaoDaChuyen(DateTimeOffset luc)
        {
            ChiChuyenKhoanThuCong();
            if (Status == PaymentIntentStatus.AwaitingApproval)
            {
                return;
            }

            if (Status != PaymentIntentStatus.Pending)
            {
                throw new RuleViolationException("lenh_nap_da_chot", "Lenh nap da chot (" + Status + ").");
            }

            Status = PaymentIntentStatus.AwaitingApproval;
            TransferredAt = luc;
        }

        /// <summary>
        /// Admin (hoac he thong theo nguong) xac nhan tien da ve tai khoan cong ty.
        /// maGiaoDich = ma giao dich tren sao ke; UNIQUE (provider, ma) chan dung mot
        /// khoan tien ve cho HAI lenh nap. Khong co thi dung ma lenh.
        /// </summary>
        public void DuyetChuyenKhoan(Guid? nguoiDuyet, string? maGiaoDich, DateTimeOffset luc)
        {
            ChiChuyenKhoanThuCong();
            if (Status != PaymentIntentStatus.Pending && Status != PaymentIntentStatus.AwaitingApproval)
            {
                throw new RuleViolationException("lenh_nap_da_chot", "Lenh nap da chot (" + Status + ").");
            }

            string ma = maGiaoDich == null ? string.Empty : maGiaoDich.Trim();
            if (ma.Length > 100)
            {
                throw new InvalidValueException("ma_giao_dich_qua_dai", "Ma giao dich toi da 100 ky tu.");
            }

            Status = PaymentIntentStatus.Succeeded;
            ProviderTxnId = ma.Length == 0 ? "MANUAL-" + Id.ToString("N") : ma;
            ReviewedBy = nguoiDuyet;
            CompletedAt = luc;
        }

        public void TuChoiChuyenKhoan(Guid nguoiDuyet, string lyDo, DateTimeOffset luc)
        {
            ChiChuyenKhoanThuCong();
            if (Status != PaymentIntentStatus.Pending && Status != PaymentIntentStatus.AwaitingApproval)
            {
                throw new RuleViolationException("lenh_nap_da_chot", "Lenh nap da chot (" + Status + ").");
            }

            string ly = lyDo == null ? string.Empty : lyDo.Trim();
            if (ly.Length == 0 || ly.Length > DoDaiLyDoToiDa)
            {
                throw new InvalidValueException("thieu_ly_do", "Tu choi lenh nap phai ghi ly do (1-" + DoDaiLyDoToiDa + " ky tu).");
            }

            Status = PaymentIntentStatus.Rejected;
            ReviewedBy = nguoiDuyet;
            RejectReason = ly;
            CompletedAt = luc;
        }

        private void ChiChuyenKhoanThuCong()
        {
            if (!LaChuyenKhoanThuCong)
            {
                throw new RuleViolationException("khong_phai_chuyen_khoan", "Lenh nap qua cong thanh toan — chi cong moi xac nhan duoc.");
            }
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
