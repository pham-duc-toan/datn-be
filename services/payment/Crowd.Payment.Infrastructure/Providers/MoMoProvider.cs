using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Crowd.Payment.Infrastructure.Providers
{
    /// <summary>Cau hinh "PaymentProviders:MoMo". Thieu PartnerCode / AccessKey / SecretKey thi khong dang ky.</summary>
    public sealed class MoMoOptions
    {
        public const string SectionName = "PaymentProviders:MoMo";

        public string PartnerCode { get; set; } = string.Empty;

        public string AccessKey { get; set; } = string.Empty;

        /// <summary>Khoa ky HMAC-SHA256. KHONG commit gia tri that.</summary>
        public string SecretKey { get; set; } = string.Empty;

        /// <summary>Goc API. Moi truong thu cua MoMo: https://test-payment.momo.vn</summary>
        public string ApiUrl { get; set; } = string.Empty;

        /// <summary>Trang frontend MoMo chuyen nguoi dung ve (chi hien thi).</summary>
        public string RedirectUrl { get; set; } = string.Empty;

        /// <summary>URL IPN cong khai cua payment-svc (MoMo goi POST). Gui kem moi lenh tao.</summary>
        public string IpnUrl { get; set; } = string.Empty;

        /// <summary>captureWallet (vi MoMo) | payWithMethod (chon phuong thuc).</summary>
        public string RequestType { get; set; } = "captureWallet";

        public bool DuCauHinh
        {
            get
            {
                return !string.IsNullOrWhiteSpace(PartnerCode) && !string.IsNullOrWhiteSpace(AccessKey)
                    && !string.IsNullOrWhiteSpace(SecretKey) && !string.IsNullOrWhiteSpace(ApiUrl);
            }
        }
    }

    /// <summary>Ky theo tai lieu MoMo API v2: HMAC-SHA256 tren chuoi "k=v&amp;..." theo thu tu chu cai co dinh.</summary>
    public static class MoMoKy
    {
        public static string Hmac(string khoa, string duLieu)
        {
            byte[] mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(khoa), Encoding.UTF8.GetBytes(duLieu));
            return Convert.ToHexStringLower(mac);
        }

        public static string ChuoiKyTao(string accessKey, long amount, string extraData, string ipnUrl, string orderId,
            string orderInfo, string partnerCode, string redirectUrl, string requestId, string requestType)
        {
            return "accessKey=" + accessKey + "&amount=" + amount.ToString(CultureInfo.InvariantCulture) + "&extraData=" + extraData
                + "&ipnUrl=" + ipnUrl + "&orderId=" + orderId + "&orderInfo=" + orderInfo + "&partnerCode=" + partnerCode
                + "&redirectUrl=" + redirectUrl + "&requestId=" + requestId + "&requestType=" + requestType;
        }

        public static string ChuoiKyIpn(string accessKey, IReadOnlyDictionary<string, string> p)
        {
            return "accessKey=" + accessKey + "&amount=" + Lay(p, "amount") + "&extraData=" + Lay(p, "extraData")
                + "&message=" + Lay(p, "message") + "&orderId=" + Lay(p, "orderId") + "&orderInfo=" + Lay(p, "orderInfo")
                + "&orderType=" + Lay(p, "orderType") + "&partnerCode=" + Lay(p, "partnerCode") + "&payType=" + Lay(p, "payType")
                + "&requestId=" + Lay(p, "requestId") + "&responseTime=" + Lay(p, "responseTime") + "&resultCode=" + Lay(p, "resultCode")
                + "&transId=" + Lay(p, "transId");
        }

        public static string ChuoiKyTraCuu(string accessKey, string orderId, string partnerCode, string requestId)
        {
            return "accessKey=" + accessKey + "&orderId=" + orderId + "&partnerCode=" + partnerCode + "&requestId=" + requestId;
        }

        public static bool KhopChuKy(string mongDoi, string? nhanDuoc)
        {
            if (string.IsNullOrEmpty(nhanDuoc))
            {
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(mongDoi), Encoding.ASCII.GetBytes(nhanDuoc.Trim().ToLowerInvariant()));
        }

        public static string Lay(IReadOnlyDictionary<string, string> d, string khoa)
        {
            string? v;
            return d.TryGetValue(khoa, out v) && v != null ? v : string.Empty;
        }
    }

    /// <summary>
    /// Adapter vi MoMo (API v2):
    ///   - Tao lenh: POST /v2/gateway/api/create co chu ky → MoMo tra payUrl (khac VNPay: PHAI goi API).
    ///   - Ket qua: IPN POST JSON co chu ky toi IpnUrl; payment-svc tra 204.
    ///   - Tra cuu: POST /v2/gateway/api/query. resultCode 0 = da tra; 1000 / 7000 = dang cho nguoi dung.
    /// So tien tinh bang dong.
    /// </summary>
    public sealed class MoMoProvider : ICongNap
    {
        public const string Ten = "momo";

        private static readonly JsonSerializerOptions JsonApi = new JsonSerializerOptions();

        private readonly MoMoOptions _options;
        private readonly HttpClient _http;
        private readonly ILogger<MoMoProvider> _logger;

        public MoMoProvider(IOptions<MoMoOptions> options, HttpClient http, ILogger<MoMoProvider> logger)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (http == null)
            {
                throw new ArgumentNullException(nameof(http));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _options = options.Value;
            _http = http;
            _logger = logger;
        }

        public string Name
        {
            get { return Ten; }
        }

        /// <summary>Goi API tao don. Loi (mang / MoMo tu choi) → nem CongThanhToanException.</summary>
        public async Task<string> TaoLinkThanhToanAsync(YeuCauThanhToan yeuCau, CancellationToken ct)
        {
            if (yeuCau == null)
            {
                throw new ArgumentNullException(nameof(yeuCau));
            }

            // requestId khac nhau moi lan goi; orderId co dinh = lenh nap (MoMo chan trung orderId).
            string requestId = Guid.NewGuid().ToString("N");
            string extraData = string.Empty;
            string chuKy = MoMoKy.Hmac(_options.SecretKey, MoMoKy.ChuoiKyTao(
                _options.AccessKey, yeuCau.AmountVnd, extraData, _options.IpnUrl, yeuCau.MaDon, yeuCau.MoTa,
                _options.PartnerCode, _options.RedirectUrl, requestId, _options.RequestType));

            Dictionary<string, object> body = new Dictionary<string, object>(StringComparer.Ordinal);
            body["partnerCode"] = _options.PartnerCode;
            body["requestId"] = requestId;
            body["amount"] = yeuCau.AmountVnd;
            body["orderId"] = yeuCau.MaDon;
            body["orderInfo"] = yeuCau.MoTa;
            body["redirectUrl"] = _options.RedirectUrl;
            body["ipnUrl"] = _options.IpnUrl;
            body["requestType"] = _options.RequestType;
            body["extraData"] = extraData;
            body["lang"] = "vi";
            body["signature"] = chuKy;

            Dictionary<string, string>? phanHoi;
            try
            {
                using (HttpResponseMessage r = await _http.PostAsJsonAsync(Url("/v2/gateway/api/create"), body, JsonApi, ct))
                {
                    phanHoi = await VnPayProvider.DocJsonPhangAsync(r, ct);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
            {
                throw new CongThanhToanException("MoMo khong phan hoi khi tao lenh.", ex);
            }

            if (phanHoi == null || MoMoKy.Lay(phanHoi, "resultCode") != "0" || MoMoKy.Lay(phanHoi, "payUrl").Length == 0)
            {
                string ma = phanHoi == null ? "?" : MoMoKy.Lay(phanHoi, "resultCode") + " " + MoMoKy.Lay(phanHoi, "message");
                throw new CongThanhToanException("MoMo tu choi tao lenh: " + ma, null);
            }

            return MoMoKy.Lay(phanHoi, "payUrl");
        }

        /// <summary>Doc IPN (than JSON MoMo POST toi). Chu ky sai → null.</summary>
        public ThongBaoNap? DocIpn(IReadOnlyDictionary<string, string> p)
        {
            if (p == null)
            {
                throw new ArgumentNullException(nameof(p));
            }

            string mongDoi = MoMoKy.Hmac(_options.SecretKey, MoMoKy.ChuoiKyIpn(_options.AccessKey, p));
            if (!MoMoKy.KhopChuKy(mongDoi, MoMoKy.Lay(p, "signature"))
                || MoMoKy.Lay(p, "partnerCode") != _options.PartnerCode)
            {
                return null;
            }

            Guid intentId;
            long amount;
            if (!Guid.TryParseExact(MoMoKy.Lay(p, "orderId"), "N", out intentId)
                || !long.TryParse(MoMoKy.Lay(p, "amount"), NumberStyles.None, CultureInfo.InvariantCulture, out amount))
            {
                return null;
            }

            string ma = MoMoKy.Lay(p, "resultCode");
            bool thanhCong = ma == "0";
            return new ThongBaoNap
            {
                IntentId = intentId,
                AmountVnd = amount,
                ThanhCong = thanhCong,
                ProviderTxnId = thanhCong ? MoMoKy.Lay(p, "transId") : string.Empty,
                MaKetQua = ma,
            };
        }

        public async Task<KetQuaTraCuuNap> TraCuuNapAsync(YeuCauThanhToan yeuCau, CancellationToken ct)
        {
            if (yeuCau == null)
            {
                throw new ArgumentNullException(nameof(yeuCau));
            }

            string requestId = Guid.NewGuid().ToString("N");
            Dictionary<string, object> body = new Dictionary<string, object>(StringComparer.Ordinal);
            body["partnerCode"] = _options.PartnerCode;
            body["requestId"] = requestId;
            body["orderId"] = yeuCau.MaDon;
            body["lang"] = "vi";
            body["signature"] = MoMoKy.Hmac(_options.SecretKey, MoMoKy.ChuoiKyTraCuu(_options.AccessKey, yeuCau.MaDon, _options.PartnerCode, requestId));

            Dictionary<string, string>? p;
            try
            {
                using (HttpResponseMessage r = await _http.PostAsJsonAsync(Url("/v2/gateway/api/query"), body, JsonApi, ct))
                {
                    p = await VnPayProvider.DocJsonPhangAsync(r, ct);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
            {
                _logger.LogWarning(ex, "Tra cuu MoMo lenh {MaDon} loi", yeuCau.MaDon);
                return KetQuaTraCuuNap.KhongRo("loi_mang");
            }

            if (p == null || MoMoKy.Lay(p, "orderId") != yeuCau.MaDon)
            {
                return KetQuaTraCuuNap.KhongRo("phan_hoi_khong_hop_le");
            }

            string ma = MoMoKy.Lay(p, "resultCode");
            if (ma == "0")
            {
                long amount;
                long.TryParse(MoMoKy.Lay(p, "amount"), NumberStyles.None, CultureInfo.InvariantCulture, out amount);
                return new KetQuaTraCuuNap(TrangThaiTraCuu.DaThanhToan, amount, MoMoKy.Lay(p, "transId"), ma);
            }

            // 1000: da tao, cho nguoi dung xac nhan; 7000 / 7002: dang xu ly; 9000: da uy quyen, cho thu.
            if (ma == "1000" || ma == "7000" || ma == "7002" || ma == "9000")
            {
                return new KetQuaTraCuuNap(TrangThaiTraCuu.ChuaThanhToan, 0, string.Empty, ma);
            }

            // Loi he thong / tham so cua chinh lenh tra cuu: khong ket luan ve giao dich.
            if (ma == "10" || ma == "11" || ma == "12" || ma == "13" || ma == "20" || ma == "21" || ma == "22" || ma == "40" || ma == "99")
            {
                return KetQuaTraCuuNap.KhongRo("ma_" + ma);
            }

            return new KetQuaTraCuuNap(TrangThaiTraCuu.ThatBai, 0, string.Empty, ma);
        }

        private string Url(string duong)
        {
            return _options.ApiUrl.TrimEnd('/') + duong;
        }
    }

    /// <summary>Cong thanh toan loi khi TAO lenh (khong ket noi duoc / tu choi) → 502 cho client.</summary>
    public sealed class CongThanhToanException : Exception
    {
        public CongThanhToanException(string message, Exception? inner)
            : base(message, inner)
        {
        }
    }
}
