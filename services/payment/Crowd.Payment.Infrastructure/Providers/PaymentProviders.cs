using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Crowd.Payment.Infrastructure.Providers
{
    public enum ProviderOutcome
    {
        Succeeded,
        Failed,

        /// <summary>Khong biet (timeout, dut mang) — PHAI tra cuu lai, khong duoc doan.</summary>
        Unknown,
    }

    public sealed class ProviderPayoutResult
    {
        public ProviderPayoutResult(ProviderOutcome outcome, string? providerTxnId, string? reason)
        {
            Outcome = outcome;
            ProviderTxnId = providerTxnId;
            Reason = reason;
        }

        public ProviderOutcome Outcome { get; }

        public string? ProviderTxnId { get; }

        public string? Reason { get; }
    }

    /// <summary>
    /// Cong thanh toan. payment-svc chi biet interface nay — lop Infrastructure tach
    /// adapter VNPay/MoMo ra sau no de TEST DUOC ma khong can cong that (ly do
    /// payment-svc la service 4 project, docs 6.1).
    /// </summary>
    public interface IPaymentProvider
    {
        string Name { get; }

        /// <summary>Link trang thanh toan cua cong cho doanh nghiep bam vao.</summary>
        string TaoLinkThanhToan(Guid intentId, long amountVnd);

        /// <summary>Kiem chu ky webhook. Sai → KHONG tin bat cu gi trong body.</summary>
        bool KiemChuKy(string body, string? chuKy);

        /// <summary>Chuyen tien ra. referenceId la khoa idempotency phia cong.</summary>
        Task<ProviderPayoutResult> ChuyenTienAsync(Guid referenceId, long amountVnd, string bankAccount, CancellationToken ct);

        /// <summary>Tra cuu ket qua lenh chuyen da gui (VD-M-09).</summary>
        Task<ProviderPayoutResult> TraCuuAsync(Guid referenceId, string bankAccount, CancellationToken ct);
    }

    public sealed class SandboxOptions
    {
        public const string SectionName = "PaymentProviders:Sandbox";

        /// <summary>Khoa bi mat chung voi cong — dung ky va kiem webhook (HMAC-SHA256).</summary>
        public string Secret { get; set; } = string.Empty;

        /// <summary>Goc URL trang thanh toan gia lap.</summary>
        public string CheckoutBaseUrl { get; set; } = "http://localhost:8080/payments/sandbox/checkout/";
    }

    /// <summary>
    /// CONG GIA LAP — hanh xu nhu VNPay o nhung cho quan trong:
    ///   - webhook ky HMAC-SHA256 bang khoa bi mat chung;
    ///   - chuyen tien idempotent theo referenceId;
    ///   - so tai khoan chua "FAIL"    → cong tu choi (tai khoan khong ton tai);
    ///   - so tai khoan chua "TIMEOUT" → lan goi dau KHONG ro ket qua (gia lap dut
    ///     mang), tra cuu sau do thi thanh cong — de thu duong tra cuu cua VD-M-09.
    /// Ket qua suy ra TAT DINH tu tham so, nen khong can luu trang thai.
    /// </summary>
    public sealed class SandboxPaymentProvider : IPaymentProvider
    {
        private readonly SandboxOptions _options;

        public SandboxPaymentProvider(IOptions<SandboxOptions> options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            _options = options.Value;

            if (string.IsNullOrWhiteSpace(_options.Secret))
            {
                throw new InvalidOperationException("Thieu PaymentProviders:Sandbox:Secret.");
            }
        }

        public string Name
        {
            get { return "sandbox"; }
        }

        public string TaoLinkThanhToan(Guid intentId, long amountVnd)
        {
            return _options.CheckoutBaseUrl + intentId + "?amount=" + amountVnd;
        }

        /// <summary>HMAC-SHA256(secret, body) dang hex thuong.</summary>
        public string Ky(string body)
        {
            byte[] khoa = Encoding.UTF8.GetBytes(_options.Secret);
            byte[] mac = HMACSHA256.HashData(khoa, Encoding.UTF8.GetBytes(body ?? string.Empty));
            return Convert.ToHexStringLower(mac);
        }

        public bool KiemChuKy(string body, string? chuKy)
        {
            if (string.IsNullOrEmpty(chuKy))
            {
                return false;
            }

            // So sanh THOI GIAN HANG SO: so sanh chuoi thuong dung o ky tu sai dau
            // tien, do thoi gian phan hoi la doan duoc chu ky tung ky tu mot.
            byte[] mongDoi = Encoding.ASCII.GetBytes(Ky(body));
            byte[] nhanDuoc = Encoding.ASCII.GetBytes(chuKy.Trim().ToLowerInvariant());
            return CryptographicOperations.FixedTimeEquals(mongDoi, nhanDuoc);
        }

        public Task<ProviderPayoutResult> ChuyenTienAsync(Guid referenceId, long amountVnd, string bankAccount, CancellationToken ct)
        {
            if (bankAccount != null && bankAccount.Contains("TIMEOUT", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new ProviderPayoutResult(ProviderOutcome.Unknown, null, "Het thoi gian cho cong phan hoi"));
            }

            return Task.FromResult(KetQua(referenceId, bankAccount));
        }

        public Task<ProviderPayoutResult> TraCuuAsync(Guid referenceId, string bankAccount, CancellationToken ct)
        {
            return Task.FromResult(KetQua(referenceId, bankAccount));
        }

        private static ProviderPayoutResult KetQua(Guid referenceId, string? bankAccount)
        {
            if (bankAccount != null && bankAccount.Contains("FAIL", StringComparison.OrdinalIgnoreCase))
            {
                return new ProviderPayoutResult(ProviderOutcome.Failed, null, "Tai khoan nhan khong ton tai");
            }

            return new ProviderPayoutResult(ProviderOutcome.Succeeded, "SBX-P-" + referenceId.ToString("N"), null);
        }
    }
}
