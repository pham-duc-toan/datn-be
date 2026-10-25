using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    /// Cong CHI tien ra (lenh rut cua labeler / sharer). Chieu NAP tien vao nam o ICongNap
    /// (sandbox / VNPay / MoMo). payment-svc chi biet interface — lop Infrastructure tach adapter
    /// ra sau no de TEST DUOC ma khong can cong that (ly do payment-svc la service 4 project, docs 6.1).
    /// </summary>
    public interface IPaymentProvider
    {
        string Name { get; }

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

        /// <summary>Dia chi gateway trinh duyet thay — cong gia lap MoMo dung de dung payUrl.</summary>
        public string PublicUrl { get; set; } = "http://localhost:8080";
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
    public sealed class SandboxPaymentProvider : IPaymentProvider, ICongNap
    {
        private static readonly JsonSerializerOptions JsonWebhook = new JsonSerializerOptions(JsonSerializerDefaults.Web);

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

        public Task<string> TaoLinkThanhToanAsync(YeuCauThanhToan yeuCau, CancellationToken ct)
        {
            if (yeuCau == null)
            {
                throw new ArgumentNullException(nameof(yeuCau));
            }

            return Task.FromResult(_options.CheckoutBaseUrl + yeuCau.IntentId + "?amount=" + yeuCau.AmountVnd);
        }

        /// <summary>
        /// Cong gia lap khong luu giao dich nen khong tra cuu duoc: KhongRo → lenh treo duoc doi soat
        /// danh dau het han khi qua payment.deposit_expire_after (khong bao gio tu cong tien).
        /// </summary>
        public Task<KetQuaTraCuuNap> TraCuuNapAsync(YeuCauThanhToan yeuCau, CancellationToken ct)
        {
            return Task.FromResult(KetQuaTraCuuNap.KhongRo("sandbox_khong_tra_cuu"));
        }

        /// <summary>
        /// Doc webhook sandbox: kiem chu ky tren CHINH chuoi byte goc cua body (khong kiem tren object
        /// da parse — thu tu truong, khoang trang khac nhau la chu ky lech). Sai → null.
        /// </summary>
        public ThongBaoNap? DocWebhook(string body, string? chuKy)
        {
            if (!KiemChuKy(body, chuKy))
            {
                return null;
            }

            SandboxWebhook? w;
            try
            {
                w = JsonSerializer.Deserialize<SandboxWebhook>(body, JsonWebhook);
            }
            catch (JsonException)
            {
                return null;
            }

            if (w == null)
            {
                return null;
            }

            bool thanhCong = string.Equals(w.Status, "success", StringComparison.OrdinalIgnoreCase);
            return new ThongBaoNap
            {
                IntentId = w.IntentId,
                AmountVnd = w.AmountVnd,
                ThanhCong = thanhCong,
                ProviderTxnId = w.ProviderTxnId ?? string.Empty,
                MaKetQua = w.Status ?? string.Empty,
            };
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
                return new ProviderPayoutResult(ProviderOutcome.Failed, null, "Tài khoản nhận không tồn tại");
            }

            return new ProviderPayoutResult(ProviderOutcome.Succeeded, "SBX-P-" + referenceId.ToString("N"), null);
        }
    }

    /// <summary>Than webhook cong sandbox (dang JSON camelCase).</summary>
    public sealed class SandboxWebhook
    {
        public Guid IntentId { get; init; }

        public long AmountVnd { get; init; }

        /// <summary>"success" | "failed"</summary>
        public string? Status { get; init; }

        public string? ProviderTxnId { get; init; }
    }
}
