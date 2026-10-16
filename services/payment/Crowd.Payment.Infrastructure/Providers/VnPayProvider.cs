using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
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
    /// <summary>Cau hinh "PaymentProviders:VnPay". Thieu TmnCode / HashSecret thi cong nay KHONG duoc dang ky.</summary>
    public sealed class VnPayOptions
    {
        public const string SectionName = "PaymentProviders:VnPay";

        /// <summary>Ma website cua merchant (VNPay cap khi dang ky).</summary>
        public string TmnCode { get; set; } = string.Empty;

        /// <summary>Khoa bi mat ky HMAC-SHA512 (VNPay cap). KHONG commit gia tri that.</summary>
        public string HashSecret { get; set; } = string.Empty;

        /// <summary>Trang thanh toan. That: https://sandbox.vnpayment.vn/paymentv2/vpcpay.html (sandbox) / https://pay.vnpay.vn/vpcpay.html.</summary>
        public string PayUrl { get; set; } = string.Empty;

        /// <summary>API tra cuu (querydr). Sandbox that: https://sandbox.vnpayment.vn/merchant_webapi/api/transaction.</summary>
        public string ApiUrl { get; set; } = string.Empty;

        /// <summary>Trang frontend VNPay chuyen nguoi dung ve sau khi tra (chi de hien ket qua, KHONG cong tien).</summary>
        public string ReturnUrl { get; set; } = string.Empty;

        /// <summary>
        /// URL IPN cua payment-svc. Voi VNPay that, URL nay khai o trang quan tri merchant chu khong
        /// gui kem lenh; o day chi de cong gia lap biet goi ve dau.
        /// </summary>
        public string IpnUrl { get; set; } = string.Empty;

        /// <summary>IP cua server gui kem lenh tra cuu (vnp_IpAddr).</summary>
        public string ServerIp { get; set; } = "127.0.0.1";

        /// <summary>Lenh thanh toan het han sau bao nhieu phut (vnp_ExpireDate).</summary>
        public int ExpireMinutes { get; set; } = 15;

        public bool DuCauHinh
        {
            get { return !string.IsNullOrWhiteSpace(TmnCode) && !string.IsNullOrWhiteSpace(HashSecret) && !string.IsNullOrWhiteSpace(PayUrl); }
        }
    }

    /// <summary>
    /// Ky va kiem chu ky theo tai lieu VNPay 2.1.0 — tach rieng de cong gia lap (dev) va unit test
    /// dung DUNG MOT cach ky voi adapter that.
    /// </summary>
    public static class VnPayKy
    {
        /// <summary>
        /// Chuoi du lieu ky cua URL thanh toan / IPN: cac tham so vnp_* (tru vnp_SecureHash,
        /// vnp_SecureHashType) khac rong, SAP XEP theo ten (ordinal), "ten=gia tri" da URL-encode
        /// (WebUtility.UrlEncode: dau cach thanh '+'), noi bang '&amp;'.
        /// </summary>
        public static string ChuoiKy(IEnumerable<KeyValuePair<string, string>> thamSo)
        {
            if (thamSo == null)
            {
                throw new ArgumentNullException(nameof(thamSo));
            }

            SortedDictionary<string, string> sapXep = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> kv in thamSo)
            {
                if (!kv.Key.StartsWith("vnp_", StringComparison.Ordinal)
                    || kv.Key == "vnp_SecureHash"
                    || kv.Key == "vnp_SecureHashType"
                    || string.IsNullOrEmpty(kv.Value))
                {
                    continue;
                }

                sapXep[kv.Key] = kv.Value;
            }

            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, string> kv in sapXep)
            {
                if (sb.Length > 0)
                {
                    sb.Append('&');
                }

                sb.Append(WebUtility.UrlEncode(kv.Key)).Append('=').Append(WebUtility.UrlEncode(kv.Value));
            }

            return sb.ToString();
        }

        /// <summary>HMAC-SHA512 dang hex thuong.</summary>
        public static string Hmac(string khoa, string duLieu)
        {
            byte[] mac = HMACSHA512.HashData(Encoding.UTF8.GetBytes(khoa), Encoding.UTF8.GetBytes(duLieu));
            return Convert.ToHexStringLower(mac);
        }

        /// <summary>So chu ky thoi gian hang so, khong phan biet hoa thuong (VNPay co the gui hex hoa).</summary>
        public static bool KhopChuKy(string mongDoi, string? nhanDuoc)
        {
            if (string.IsNullOrEmpty(nhanDuoc))
            {
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(mongDoi.ToLowerInvariant()),
                Encoding.ASCII.GetBytes(nhanDuoc.Trim().ToLowerInvariant()));
        }

        /// <summary>Chuoi ky cua YEU CAU tra cuu querydr (cac truong noi bang '|' theo thu tu tai lieu).</summary>
        public static string ChuoiKyTraCuu(string requestId, string version, string command, string tmnCode, string txnRef,
            string transactionDate, string createDate, string ipAddr, string orderInfo)
        {
            return string.Join("|", requestId, version, command, tmnCode, txnRef, transactionDate, createDate, ipAddr, orderInfo);
        }

        /// <summary>Chuoi ky cua PHAN HOI querydr — kiem de chac phan hoi dung la cua VNPay (khong bi sua giua duong).</summary>
        public static string ChuoiKyPhanHoiTraCuu(IReadOnlyDictionary<string, string> p)
        {
            string[] truong = new string[]
            {
                "vnp_ResponseId", "vnp_Command", "vnp_ResponseCode", "vnp_Message", "vnp_TmnCode", "vnp_TxnRef",
                "vnp_Amount", "vnp_BankCode", "vnp_PayDate", "vnp_TransactionNo", "vnp_TransactionType",
                "vnp_TransactionStatus", "vnp_OrderInfo", "vnp_PromotionCode", "vnp_PromotionAmount",
            };

            List<string> giaTri = new List<string>();
            foreach (string t in truong)
            {
                string? v;
                giaTri.Add(p.TryGetValue(t, out v) && v != null ? v : string.Empty);
            }

            return string.Join("|", giaTri);
        }
    }

    /// <summary>
    /// Adapter VNPay (cong thanh toan noi dia, the ATM / QR / the quoc te):
    ///   - Link thanh toan = PayUrl?{tham so}&amp;vnp_SecureHash=HMAC-SHA512 — tu ky, khong goi API.
    ///   - Ket qua: IPN (VNPay goi GET toi URL khai tren trang merchant) + ReturnUrl (trinh duyet ve frontend).
    ///     Chi IPN / tra cuu moi cong tien; ReturnUrl chi de hien thi.
    ///   - Tra cuu querydr: POST JSON co chu ky — dung de xac nhan lai so tien sau IPN (VD-S-02) va doi
    ///     soat lenh treo (VD-M-09).
    /// So tien VNPay tinh bang don vi 1/100 dong (vnp_Amount = so tien x 100).
    /// </summary>
    public sealed class VnPayProvider : ICongNap
    {
        public const string Ten = "vnpay";
        public const string PhienBan = "2.1.0";

        private static readonly JsonSerializerOptions JsonApi = new JsonSerializerOptions();

        private readonly VnPayOptions _options;
        private readonly HttpClient _http;
        private readonly TimeProvider _clock;
        private readonly ILogger<VnPayProvider> _logger;

        public VnPayProvider(IOptions<VnPayOptions> options, HttpClient http, TimeProvider clock, ILogger<VnPayProvider> logger)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (http == null)
            {
                throw new ArgumentNullException(nameof(http));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _options = options.Value;
            _http = http;
            _clock = clock;
            _logger = logger;
        }

        public string Name
        {
            get { return Ten; }
        }

        public Task<string> TaoLinkThanhToanAsync(YeuCauThanhToan yeuCau, CancellationToken ct)
        {
            if (yeuCau == null)
            {
                throw new ArgumentNullException(nameof(yeuCau));
            }

            Dictionary<string, string> p = new Dictionary<string, string>(StringComparer.Ordinal);
            p["vnp_Version"] = PhienBan;
            p["vnp_Command"] = "pay";
            p["vnp_TmnCode"] = _options.TmnCode;
            p["vnp_Amount"] = (yeuCau.AmountVnd * 100).ToString(CultureInfo.InvariantCulture);
            p["vnp_CurrCode"] = "VND";
            p["vnp_TxnRef"] = yeuCau.MaDon;
            p["vnp_OrderInfo"] = yeuCau.MoTa;
            p["vnp_OrderType"] = "other";
            p["vnp_Locale"] = "vn";
            p["vnp_ReturnUrl"] = _options.ReturnUrl;
            p["vnp_IpAddr"] = string.IsNullOrWhiteSpace(yeuCau.ClientIp) ? "127.0.0.1" : yeuCau.ClientIp;
            p["vnp_CreateDate"] = GioVietNam.Dinh(yeuCau.CreatedAt);
            p["vnp_ExpireDate"] = GioVietNam.Dinh(yeuCau.CreatedAt.AddMinutes(_options.ExpireMinutes));

            string chuoi = VnPayKy.ChuoiKy(p);
            string url = _options.PayUrl + "?" + chuoi + "&vnp_SecureHash=" + VnPayKy.Hmac(_options.HashSecret, chuoi);
            return Task.FromResult(url);
        }

        /// <summary>
        /// Doc IPN (query string VNPay goi toi). Chu ky sai → null (KHONG tin bat cu gi trong do).
        /// </summary>
        public ThongBaoNap? DocIpn(IReadOnlyDictionary<string, string> query)
        {
            if (query == null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            string? chuKy;
            query.TryGetValue("vnp_SecureHash", out chuKy);
            string mongDoi = VnPayKy.Hmac(_options.HashSecret, VnPayKy.ChuoiKy(query));
            if (!VnPayKy.KhopChuKy(mongDoi, chuKy))
            {
                return null;
            }

            string txnRef = Lay(query, "vnp_TxnRef");
            Guid intentId;
            long amount100;
            if (!Guid.TryParseExact(txnRef, "N", out intentId)
                || !long.TryParse(Lay(query, "vnp_Amount"), NumberStyles.None, CultureInfo.InvariantCulture, out amount100))
            {
                return null;
            }

            string maKetQua = Lay(query, "vnp_ResponseCode");
            bool thanhCong = maKetQua == "00" && Lay(query, "vnp_TransactionStatus") == "00";
            return new ThongBaoNap
            {
                IntentId = intentId,
                AmountVnd = amount100 / 100,
                ThanhCong = thanhCong,
                ProviderTxnId = thanhCong ? Lay(query, "vnp_TransactionNo") : string.Empty,
                MaKetQua = maKetQua,
            };
        }

        public async Task<KetQuaTraCuuNap> TraCuuNapAsync(YeuCauThanhToan yeuCau, CancellationToken ct)
        {
            if (yeuCau == null)
            {
                throw new ArgumentNullException(nameof(yeuCau));
            }

            if (string.IsNullOrWhiteSpace(_options.ApiUrl))
            {
                return KetQuaTraCuuNap.KhongRo("chua_cau_hinh_api");
            }

            string requestId = Guid.NewGuid().ToString("N");
            string createDate = GioVietNam.Dinh(_clock.GetUtcNow());
            string transactionDate = GioVietNam.Dinh(yeuCau.CreatedAt);
            string orderInfo = "Tra cuu " + yeuCau.MaDon;
            string ky = VnPayKy.Hmac(_options.HashSecret, VnPayKy.ChuoiKyTraCuu(
                requestId, PhienBan, "querydr", _options.TmnCode, yeuCau.MaDon, transactionDate, createDate, _options.ServerIp, orderInfo));

            Dictionary<string, string> body = new Dictionary<string, string>(StringComparer.Ordinal);
            body["vnp_RequestId"] = requestId;
            body["vnp_Version"] = PhienBan;
            body["vnp_Command"] = "querydr";
            body["vnp_TmnCode"] = _options.TmnCode;
            body["vnp_TxnRef"] = yeuCau.MaDon;
            body["vnp_OrderInfo"] = orderInfo;
            body["vnp_TransactionDate"] = transactionDate;
            body["vnp_CreateDate"] = createDate;
            body["vnp_IpAddr"] = _options.ServerIp;
            body["vnp_SecureHash"] = ky;

            Dictionary<string, string>? phanHoi;
            try
            {
                using (HttpResponseMessage r = await _http.PostAsJsonAsync(_options.ApiUrl, body, JsonApi, ct))
                {
                    if (!r.IsSuccessStatusCode)
                    {
                        return KetQuaTraCuuNap.KhongRo("http_" + (int)r.StatusCode);
                    }

                    phanHoi = await DocJsonPhangAsync(r, ct);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
            {
                _logger.LogWarning(ex, "Tra cuu VNPay lenh {MaDon} loi", yeuCau.MaDon);
                return KetQuaTraCuuNap.KhongRo("loi_mang");
            }

            if (phanHoi == null)
            {
                return KetQuaTraCuuNap.KhongRo("phan_hoi_rong");
            }

            string? chuKy;
            phanHoi.TryGetValue("vnp_SecureHash", out chuKy);
            if (!VnPayKy.KhopChuKy(VnPayKy.Hmac(_options.HashSecret, VnPayKy.ChuoiKyPhanHoiTraCuu(phanHoi)), chuKy))
            {
                _logger.LogWarning("Phan hoi tra cuu VNPay lenh {MaDon} SAI chu ky — bo qua", yeuCau.MaDon);
                return KetQuaTraCuuNap.KhongRo("phan_hoi_sai_chu_ky");
            }

            string maPhanHoi = Lay(phanHoi, "vnp_ResponseCode");
            if (maPhanHoi == "91")
            {
                // 91: khong tim thay giao dich = nguoi dung chua bam tra tien.
                return new KetQuaTraCuuNap(TrangThaiTraCuu.ChuaThanhToan, 0, string.Empty, maPhanHoi);
            }

            if (maPhanHoi != "00")
            {
                return KetQuaTraCuuNap.KhongRo("ma_" + maPhanHoi);
            }

            long amount100;
            long.TryParse(Lay(phanHoi, "vnp_Amount"), NumberStyles.None, CultureInfo.InvariantCulture, out amount100);
            string trangThai = Lay(phanHoi, "vnp_TransactionStatus");
            if (trangThai == "00")
            {
                return new KetQuaTraCuuNap(TrangThaiTraCuu.DaThanhToan, amount100 / 100, Lay(phanHoi, "vnp_TransactionNo"), trangThai);
            }

            // 01: chua hoan tat; 02: loi; con lai (04 dao, 05 dang hoan, 07 nghi gian lan, 09 tu choi hoan...).
            if (trangThai == "01")
            {
                return new KetQuaTraCuuNap(TrangThaiTraCuu.ChuaThanhToan, 0, string.Empty, trangThai);
            }

            return new KetQuaTraCuuNap(TrangThaiTraCuu.ThatBai, 0, string.Empty, trangThai);
        }

        /// <summary>Doc JSON phang {ten: chuoi|so} thanh tu dien chuoi.</summary>
        public static async Task<Dictionary<string, string>?> DocJsonPhangAsync(HttpResponseMessage r, CancellationToken ct)
        {
            using (JsonDocument doc = await JsonDocument.ParseAsync(await r.Content.ReadAsStreamAsync(ct), default, ct))
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (JsonProperty p in doc.RootElement.EnumerateObject())
                {
                    d[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : p.Value.GetRawText();
                }

                return d;
            }
        }

        private static string Lay(IReadOnlyDictionary<string, string> d, string khoa)
        {
            string? v;
            return d.TryGetValue(khoa, out v) && v != null ? v : string.Empty;
        }
    }
}
