using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Crowd.Payment.Infrastructure.Providers
{
    /// <summary>Cau hinh "PaymentProviders:ZaloPay". Thieu AppId / Key1 / Key2 / ApiUrl thi KHONG dang ky.</summary>
    public sealed class ZaloPayOptions
    {
        public const string SectionName = "PaymentProviders:ZaloPay";

        /// <summary>Ma ung dung merchant (ZaloPay cap).</summary>
        public string AppId { get; set; } = string.Empty;

        /// <summary>Khoa ky YEU CAU gui ZaloPay (tao don, tra cuu). KHONG commit gia tri that.</summary>
        public string Key1 { get; set; } = string.Empty;

        /// <summary>Khoa kiem chu ky CALLBACK ZaloPay goi ve. KHONG commit gia tri that.</summary>
        public string Key2 { get; set; } = string.Empty;

        /// <summary>Goc API. Sandbox: https://sb-openapi.zalopay.vn — that: https://openapi.zalopay.vn</summary>
        public string ApiUrl { get; set; } = string.Empty;

        /// <summary>Trang frontend ZaloPay chuyen nguoi dung ve (chi hien thi, KHONG cong tien).</summary>
        public string RedirectUrl { get; set; } = string.Empty;

        /// <summary>URL callback cong khai cua payment-svc (ZaloPay goi POST). Gui kem moi lenh tao.</summary>
        public string CallbackUrl { get; set; } = string.Empty;

        /// <summary>app_user gui ZaloPay (dinh danh nguoi tra phia merchant, toi da 50 ky tu).</summary>
        public string AppUser { get; set; } = "crowdlabel";

        public bool DuCauHinh
        {
            get
            {
                return !string.IsNullOrWhiteSpace(AppId) && !string.IsNullOrWhiteSpace(Key1)
                    && !string.IsNullOrWhiteSpace(Key2) && !string.IsNullOrWhiteSpace(ApiUrl);
            }
        }
    }

    /// <summary>Ky / kiem chu ky va ma don theo tai lieu ZaloPay API v2 (HMAC-SHA256, hex thuong).</summary>
    public static class ZaloPayKy
    {
        public static string Hmac(string khoa, string duLieu)
        {
            byte[] mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(khoa), Encoding.UTF8.GetBytes(duLieu));
            return Convert.ToHexStringLower(mac);
        }

        /// <summary>mac tao don = HMAC(key1, app_id|app_trans_id|app_user|amount|app_time|embed_data|item).</summary>
        public static string ChuoiKyTao(string appId, string appTransId, string appUser, long amount, long appTime, string embedData, string item)
        {
            return string.Join(
                "|",
                appId,
                appTransId,
                appUser,
                amount.ToString(CultureInfo.InvariantCulture),
                appTime.ToString(CultureInfo.InvariantCulture),
                embedData,
                item);
        }

        /// <summary>mac tra cuu = HMAC(key1, app_id|app_trans_id|key1).</summary>
        public static string ChuoiKyTraCuu(string appId, string appTransId, string key1)
        {
            return appId + "|" + appTransId + "|" + key1;
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

        /// <summary>
        /// app_trans_id = "yyMMdd_" (ngay tao lenh, gio VN — ZaloPay BAT BUOC tien to ngay) + 32 hex cua
        /// intentId = 39 ky tu (gioi han 40). Suy ra tu lenh nap nen tra cuu dung lai duoc.
        /// </summary>
        public static string MaGiaoDich(YeuCauThanhToan yeuCau)
        {
            if (yeuCau == null)
            {
                throw new ArgumentNullException(nameof(yeuCau));
            }

            string ngay = yeuCau.CreatedAt.ToOffset(TimeSpan.FromHours(7)).ToString("yyMMdd", CultureInfo.InvariantCulture);
            return ngay + "_" + yeuCau.MaDon;
        }

        /// <summary>Tach intentId tu app_trans_id ("yyMMdd_&lt;32 hex&gt;"). Sai dang → false.</summary>
        public static bool TachIntentId(string? appTransId, out Guid intentId)
        {
            intentId = Guid.Empty;
            if (string.IsNullOrEmpty(appTransId))
            {
                return false;
            }

            int gach = appTransId.IndexOf('_', StringComparison.Ordinal);
            return gach > 0 && Guid.TryParseExact(appTransId.Substring(gach + 1), "N", out intentId);
        }
    }

    /// <summary>
    /// Adapter ZaloPay (API v2, cong thanh toan — vi ZaloPay / QR / the ATM / the quoc te):
    ///   - Tao lenh: POST /v2/create (form, mac key1) → ZaloPay tra order_url (giong MoMo: PHAI goi API).
    ///   - Ket qua: callback POST JSON {data, mac, type} toi CallbackUrl, mac = HMAC(key2, data). ZaloPay
    ///     CHI goi khi thanh toan THANH CONG; tra {return_code: 1} la xong, khac 1/2 thi ZaloPay goi lai.
    ///   - Tra cuu: POST /v2/query. return_code 1 = da tra; 3 = chua tra / dang xu ly; 2 = that bai.
    /// So tien tinh bang dong.
    /// </summary>
    public sealed class ZaloPayProvider : ICongNap
    {
        public const string Ten = "zalopay";

        /// <summary>Danh sach hang hoa (item) — nap vi khong co hang, de mang rong.</summary>
        private const string Item = "[]";

        private readonly ZaloPayOptions _options;
        private readonly HttpClient _http;
        private readonly TimeProvider _clock;
        private readonly ILogger<ZaloPayProvider> _logger;

        public ZaloPayProvider(IOptions<ZaloPayOptions> options, HttpClient http, TimeProvider clock, ILogger<ZaloPayProvider> logger)
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

        /// <summary>Goi API tao don. Loi (mang / ZaloPay tu choi) → nem CongThanhToanException.</summary>
        public async Task<string> TaoLinkThanhToanAsync(YeuCauThanhToan yeuCau, CancellationToken ct)
        {
            if (yeuCau == null)
            {
                throw new ArgumentNullException(nameof(yeuCau));
            }

            string appTransId = ZaloPayKy.MaGiaoDich(yeuCau);
            long appTime = _clock.GetUtcNow().ToUnixTimeMilliseconds();

            // embed_data la CHUOI JSON — chuoi nay vua gui di vua nam trong mac nen phai dung y het.
            // preferred_payment_method rong: trang ZaloPay cho chon moi phuong thuc (vi, QR, the ATM, the quoc te).
            string embedData = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                { "redirecturl", _options.RedirectUrl },
                { "preferred_payment_method", Array.Empty<string>() },
            });
            string mac = ZaloPayKy.Hmac(_options.Key1, ZaloPayKy.ChuoiKyTao(
                _options.AppId, appTransId, _options.AppUser, yeuCau.AmountVnd, appTime, embedData, Item));

            Dictionary<string, string> form = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "app_id", _options.AppId },
                { "app_user", _options.AppUser },
                { "app_trans_id", appTransId },
                { "app_time", appTime.ToString(CultureInfo.InvariantCulture) },
                { "amount", yeuCau.AmountVnd.ToString(CultureInfo.InvariantCulture) },
                { "item", Item },
                { "embed_data", embedData },
                { "description", yeuCau.MoTa },
                { "bank_code", string.Empty },
                { "callback_url", _options.CallbackUrl },
                { "mac", mac },
            };

            Dictionary<string, string>? phanHoi;
            try
            {
                using (FormUrlEncodedContent noiDung = new FormUrlEncodedContent(form))
                using (HttpResponseMessage r = await _http.PostAsync(Url("/v2/create"), noiDung, ct))
                {
                    phanHoi = await VnPayProvider.DocJsonPhangAsync(r, ct);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
            {
                throw new CongThanhToanException("ZaloPay không phản hồi khi tạo lệnh.", ex);
            }

            if (phanHoi == null || Lay(phanHoi, "return_code") != "1" || Lay(phanHoi, "order_url").Length == 0)
            {
                string ma = phanHoi == null
                    ? "?"
                    : Lay(phanHoi, "sub_return_code") + " " + Lay(phanHoi, "sub_return_message");
                throw new CongThanhToanException("ZaloPay từ chối tạo lệnh: " + ma, null);
            }

            return Lay(phanHoi, "order_url");
        }

        /// <summary>
        /// Doc callback (than JSON ZaloPay POST toi). mac sai / sai app / sai dinh dang → null.
        /// ZaloPay chi callback khi THANH CONG nen ThongBaoNap.ThanhCong luon true.
        /// </summary>
        public ThongBaoNap? DocCallback(string body)
        {
            Dictionary<string, string>? ngoai = DocJsonPhang(body);
            if (ngoai == null)
            {
                return null;
            }

            // Kiem mac tren CHINH chuoi "data" ZaloPay gui (khong tren object da parse).
            string data = Lay(ngoai, "data");
            if (data.Length == 0 || !ZaloPayKy.KhopChuKy(ZaloPayKy.Hmac(_options.Key2, data), Lay(ngoai, "mac")))
            {
                return null;
            }

            Dictionary<string, string>? d = DocJsonPhang(data);
            if (d == null || Lay(d, "app_id") != _options.AppId)
            {
                return null;
            }

            Guid intentId;
            long amount;
            if (!ZaloPayKy.TachIntentId(Lay(d, "app_trans_id"), out intentId)
                || !long.TryParse(Lay(d, "amount"), NumberStyles.None, CultureInfo.InvariantCulture, out amount))
            {
                return null;
            }

            return new ThongBaoNap
            {
                IntentId = intentId,
                AmountVnd = amount,
                ThanhCong = true,
                ProviderTxnId = Lay(d, "zp_trans_id"),
                MaKetQua = "1",
            };
        }

        public async Task<KetQuaTraCuuNap> TraCuuNapAsync(YeuCauThanhToan yeuCau, CancellationToken ct)
        {
            if (yeuCau == null)
            {
                throw new ArgumentNullException(nameof(yeuCau));
            }

            string appTransId = ZaloPayKy.MaGiaoDich(yeuCau);
            Dictionary<string, string> form = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "app_id", _options.AppId },
                { "app_trans_id", appTransId },
                { "mac", ZaloPayKy.Hmac(_options.Key1, ZaloPayKy.ChuoiKyTraCuu(_options.AppId, appTransId, _options.Key1)) },
            };

            Dictionary<string, string>? p;
            try
            {
                using (FormUrlEncodedContent noiDung = new FormUrlEncodedContent(form))
                using (HttpResponseMessage r = await _http.PostAsync(Url("/v2/query"), noiDung, ct))
                {
                    if (!r.IsSuccessStatusCode)
                    {
                        return KetQuaTraCuuNap.KhongRo("http_" + (int)r.StatusCode);
                    }

                    p = await VnPayProvider.DocJsonPhangAsync(r, ct);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
            {
                _logger.LogWarning(ex, "Tra cuu ZaloPay lenh {AppTransId} loi", appTransId);
                return KetQuaTraCuuNap.KhongRo("loi_mang");
            }

            if (p == null)
            {
                return KetQuaTraCuuNap.KhongRo("phan_hoi_rong");
            }

            return DocKetQuaTraCuu(p);
        }

        /// <summary>
        /// Dien giai phan hoi /v2/query (tach rieng de unit test):
        ///   return_code 1 → da tra (so tien + zp_trans_id);
        ///   return_code 3 → chua tra / dang xu ly;
        ///   return_code 2 + sub -101 (don khong ton tai) → chua tra (nguoi dung chua mo trang ZaloPay);
        ///   return_code 2 + loi cua CHINH yeu cau tra cuu (-401 tham so, -402 mac, -500 he thong) → khong ro;
        ///   return_code 2 con lai → that bai.
        /// </summary>
        public static KetQuaTraCuuNap DocKetQuaTraCuu(IReadOnlyDictionary<string, string> p)
        {
            if (p == null)
            {
                throw new ArgumentNullException(nameof(p));
            }

            string ma = Lay(p, "return_code");
            string maPhu = Lay(p, "sub_return_code");
            if (ma == "1")
            {
                long amount;
                long.TryParse(Lay(p, "amount"), NumberStyles.None, CultureInfo.InvariantCulture, out amount);
                return new KetQuaTraCuuNap(TrangThaiTraCuu.DaThanhToan, amount, Lay(p, "zp_trans_id"), ma);
            }

            if (ma == "3" || (ma == "2" && maPhu == "-101"))
            {
                return new KetQuaTraCuuNap(TrangThaiTraCuu.ChuaThanhToan, 0, string.Empty, ma + "/" + maPhu);
            }

            if (ma != "2" || maPhu == "-401" || maPhu == "-402" || maPhu == "-500")
            {
                return KetQuaTraCuuNap.KhongRo("ma_" + ma + "/" + maPhu);
            }

            return new KetQuaTraCuuNap(TrangThaiTraCuu.ThatBai, 0, string.Empty, ma + "/" + maPhu);
        }

        /// <summary>Doc JSON phang {ten: chuoi|so} trong chuoi; sai dinh dang → null.</summary>
        private static Dictionary<string, string>? DocJsonPhang(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
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
            catch (JsonException)
            {
                return null;
            }
        }

        private static string Lay(IReadOnlyDictionary<string, string> d, string khoa)
        {
            string? v;
            return d.TryGetValue(khoa, out v) && v != null ? v : string.Empty;
        }

        private string Url(string duong)
        {
            return _options.ApiUrl.TrimEnd('/') + duong;
        }
    }
}
