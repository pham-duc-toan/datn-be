using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Payment.Infrastructure.Providers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Crowd.Payment.Api.Controllers
{
    /// <summary>
    /// Giao dich ma cong gia lap dang giu (trong bo nho — khoi dong lai la mat, dung nhu cong that
    /// "khong tim thay giao dich").
    /// </summary>
    public sealed class KhoCongGiaLap
    {
        public ConcurrentDictionary<string, GiaoDichGiaLap> VnPay { get; } = new ConcurrentDictionary<string, GiaoDichGiaLap>(StringComparer.Ordinal);

        public ConcurrentDictionary<string, GiaoDichGiaLap> MoMo { get; } = new ConcurrentDictionary<string, GiaoDichGiaLap>(StringComparer.Ordinal);
    }

    public sealed class GiaoDichGiaLap
    {
        public required string MaDon { get; init; }

        public required long AmountVnd { get; init; }

        public required string OrderInfo { get; init; }

        /// <summary>pending | success | failed</summary>
        public string TrangThai { get; set; } = "pending";

        public string MaGiaoDich { get; set; } = string.Empty;

        public string IpnUrl { get; set; } = string.Empty;

        public string ReturnUrl { get; set; } = string.Empty;
    }

    /// <summary>
    /// CHI MOI TRUONG DEV / DEMO: CONG GIA LAP dong vai VNPay va MoMo de chay tron luong khong can tai
    /// khoan merchant that. Gia lap dung nhung cho payment-svc phai lam dung:
    ///   - VNPay: kiem chu ky URL thanh toan (sua vnp_Amount tren URL → "sai chu ky"), goi IPN GET co
    ///     chu ky, tra loi querydr co chu ky phan hoi;
    ///   - MoMo: kiem chu ky lenh tao, tra payUrl, goi IPN POST co chu ky, tra loi tra cuu.
    /// Dung CHUNG khoa voi adapter (cau hinh PaymentProviders:VnPay / MoMo) — vai tro "cong".
    /// Len that: tro PayUrl / ApiUrl sang cong that, cac route nay tra 404 (khong phai Development).
    /// </summary>
    [ApiController]
    [Route("payments/sandbox")]
    [AllowAnonymous]
    public sealed class CongGiaLapController : ControllerBase
    {
        private readonly KhoCongGiaLap _kho;
        private readonly VnPayOptions _vnpay;
        private readonly MoMoOptions _momo;
        private readonly SandboxOptions _sandbox;
        private readonly IHttpClientFactory _http;
        private readonly IWebHostEnvironment _env;
        private readonly TimeProvider _clock;

        public CongGiaLapController(
            KhoCongGiaLap kho,
            IOptions<VnPayOptions> vnpay,
            IOptions<MoMoOptions> momo,
            IOptions<SandboxOptions> sandbox,
            IHttpClientFactory http,
            IWebHostEnvironment env,
            TimeProvider clock)
        {
            if (kho == null)
            {
                throw new ArgumentNullException(nameof(kho));
            }

            if (vnpay == null)
            {
                throw new ArgumentNullException(nameof(vnpay));
            }

            if (momo == null)
            {
                throw new ArgumentNullException(nameof(momo));
            }

            if (http == null)
            {
                throw new ArgumentNullException(nameof(http));
            }

            if (sandbox == null)
            {
                throw new ArgumentNullException(nameof(sandbox));
            }

            if (env == null)
            {
                throw new ArgumentNullException(nameof(env));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _kho = kho;
            _vnpay = vnpay.Value;
            _momo = momo.Value;
            _sandbox = sandbox.Value;
            _http = http;
            _env = env;
            _clock = clock;
        }

        // =====================================================================
        // VNPAY
        // =====================================================================

        /// <summary>GET /payments/sandbox/vnpay/pay?vnp_... — "trang thanh toan VNPay": kiem chu ky, nho giao dich.</summary>
        [HttpGet("vnpay/pay")]
        public IActionResult VnPayTrang()
        {
            if (!_env.IsDevelopment() || !_vnpay.DuCauHinh)
            {
                return NotFound();
            }

            Dictionary<string, string> q = Query();
            string? chuKy;
            q.TryGetValue("vnp_SecureHash", out chuKy);
            if (!VnPayKy.KhopChuKy(VnPayKy.Hmac(_vnpay.HashSecret, VnPayKy.ChuoiKy(q)), chuKy))
            {
                return BadRequest(new { error = "Sai chu ky (code 97) — tham so tren URL thanh toan da bi sua." });
            }

            long amount100 = long.Parse(q["vnp_Amount"], CultureInfo.InvariantCulture);
            GiaoDichGiaLap gd = _kho.VnPay.GetOrAdd(q["vnp_TxnRef"], k => new GiaoDichGiaLap
            {
                MaDon = k,
                AmountVnd = amount100 / 100,
                OrderInfo = q["vnp_OrderInfo"],
            });
            gd.ReturnUrl = q["vnp_ReturnUrl"];
            gd.IpnUrl = _vnpay.IpnUrl;
            return Ok(new
            {
                gateway = "vnpay (gia lap)",
                txnRef = gd.MaDon,
                amountVnd = gd.AmountVnd,
                orderInfo = gd.OrderInfo,
                payAction = "POST /payments/sandbox/vnpay/pay/" + gd.MaDon + "?result=success|cancel",
            });
        }

        /// <summary>POST /payments/sandbox/vnpay/pay/{txnRef}?result=success|cancel&amp;sendIpn=true — nguoi dung bam tra / huy.</summary>
        [HttpPost("vnpay/pay/{txnRef}")]
        public async Task<IActionResult> VnPayTra(string txnRef, [FromQuery] string? result, [FromQuery] bool? sendIpn, CancellationToken ct)
        {
            GiaoDichGiaLap? gd;
            if (!_env.IsDevelopment() || !_kho.VnPay.TryGetValue(txnRef, out gd))
            {
                return NotFound();
            }

            bool tra = !string.Equals(result, "cancel", StringComparison.OrdinalIgnoreCase);
            gd.TrangThai = tra ? "success" : "failed";
            gd.MaGiaoDich = tra ? "VNP" + RandomSo(8) : string.Empty;

            Dictionary<string, string> p = new Dictionary<string, string>(StringComparer.Ordinal);
            p["vnp_Amount"] = (gd.AmountVnd * 100).ToString(CultureInfo.InvariantCulture);
            p["vnp_BankCode"] = "NCB";
            p["vnp_OrderInfo"] = gd.OrderInfo;
            p["vnp_PayDate"] = GioVietNam.Dinh(_clock.GetUtcNow());
            p["vnp_ResponseCode"] = tra ? "00" : "24";
            p["vnp_TmnCode"] = _vnpay.TmnCode;
            p["vnp_TransactionNo"] = tra ? gd.MaGiaoDich : "0";
            p["vnp_TransactionStatus"] = tra ? "00" : "02";
            p["vnp_TxnRef"] = gd.MaDon;
            string chuoi = VnPayKy.ChuoiKy(p);
            string thamSo = chuoi + "&vnp_SecureHashType=HmacSHA512&vnp_SecureHash=" + VnPayKy.Hmac(_vnpay.HashSecret, chuoi);

            string? ipn = null;
            if (sendIpn != false && gd.IpnUrl.Length > 0)
            {
                HttpClient c = _http.CreateClient();
                using (HttpResponseMessage r = await c.GetAsync(gd.IpnUrl + "?" + thamSo, ct))
                {
                    ipn = await r.Content.ReadAsStringAsync(ct);
                }
            }

            return Ok(new { redirect = gd.ReturnUrl + "?" + thamSo, ipnResponse = ipn });
        }

        /// <summary>POST /payments/sandbox/vnpay/api — querydr: kiem chu ky yeu cau, tra loi co chu ky.</summary>
        [HttpPost("vnpay/api")]
        public async Task<IActionResult> VnPayTraCuu(CancellationToken ct)
        {
            if (!_env.IsDevelopment() || !_vnpay.DuCauHinh)
            {
                return NotFound();
            }

            Dictionary<string, string>? q = WebhooksController.DocJsonPhang(await DocBodyAsync(ct));
            if (q == null)
            {
                return BadRequest();
            }

            string mongDoi = VnPayKy.Hmac(_vnpay.HashSecret, VnPayKy.ChuoiKyTraCuu(
                Lay(q, "vnp_RequestId"), Lay(q, "vnp_Version"), Lay(q, "vnp_Command"), Lay(q, "vnp_TmnCode"), Lay(q, "vnp_TxnRef"),
                Lay(q, "vnp_TransactionDate"), Lay(q, "vnp_CreateDate"), Lay(q, "vnp_IpAddr"), Lay(q, "vnp_OrderInfo")));

            Dictionary<string, string> p = new Dictionary<string, string>(StringComparer.Ordinal);
            p["vnp_ResponseId"] = Guid.NewGuid().ToString("N");
            p["vnp_Command"] = "querydr";
            p["vnp_TmnCode"] = _vnpay.TmnCode;
            p["vnp_TxnRef"] = Lay(q, "vnp_TxnRef");

            GiaoDichGiaLap? gd;
            if (!VnPayKy.KhopChuKy(mongDoi, Lay(q, "vnp_SecureHash")))
            {
                p["vnp_ResponseCode"] = "97";
                p["vnp_Message"] = "Invalid checksum";
            }
            else if (!_kho.VnPay.TryGetValue(Lay(q, "vnp_TxnRef"), out gd))
            {
                p["vnp_ResponseCode"] = "91";
                p["vnp_Message"] = "Transaction not found";
            }
            else
            {
                p["vnp_ResponseCode"] = "00";
                p["vnp_Message"] = "QueryDR Success";
                p["vnp_Amount"] = (gd.AmountVnd * 100).ToString(CultureInfo.InvariantCulture);
                p["vnp_BankCode"] = "NCB";
                p["vnp_OrderInfo"] = gd.OrderInfo;
                p["vnp_TransactionNo"] = gd.MaGiaoDich.Length > 0 ? gd.MaGiaoDich : "0";
                p["vnp_TransactionType"] = "01";
                p["vnp_TransactionStatus"] = gd.TrangThai == "success" ? "00" : (gd.TrangThai == "failed" ? "02" : "01");
                p["vnp_PayDate"] = GioVietNam.Dinh(_clock.GetUtcNow());
            }

            p["vnp_SecureHash"] = VnPayKy.Hmac(_vnpay.HashSecret, VnPayKy.ChuoiKyPhanHoiTraCuu(p));
            return Ok(p);
        }

        // =====================================================================
        // MOMO
        // =====================================================================

        /// <summary>POST /payments/sandbox/momo/v2/gateway/api/create — kiem chu ky, tra payUrl.</summary>
        [HttpPost("momo/v2/gateway/api/create")]
        public async Task<IActionResult> MoMoTao(CancellationToken ct)
        {
            if (!_env.IsDevelopment() || !_momo.DuCauHinh)
            {
                return NotFound();
            }

            Dictionary<string, string>? b = WebhooksController.DocJsonPhang(await DocBodyAsync(ct));
            if (b == null)
            {
                return BadRequest();
            }

            long amount = long.Parse(Lay(b, "amount"), CultureInfo.InvariantCulture);
            string mongDoi = MoMoKy.Hmac(_momo.SecretKey, MoMoKy.ChuoiKyTao(
                _momo.AccessKey, amount, Lay(b, "extraData"), Lay(b, "ipnUrl"), Lay(b, "orderId"), Lay(b, "orderInfo"),
                Lay(b, "partnerCode"), Lay(b, "redirectUrl"), Lay(b, "requestId"), Lay(b, "requestType")));
            if (!MoMoKy.KhopChuKy(mongDoi, Lay(b, "signature")))
            {
                return Ok(new { orderId = Lay(b, "orderId"), resultCode = 11, message = "Access denied (sai chu ky)" });
            }

            GiaoDichGiaLap gd = _kho.MoMo.GetOrAdd(Lay(b, "orderId"), k => new GiaoDichGiaLap
            {
                MaDon = k,
                AmountVnd = amount,
                OrderInfo = Lay(b, "orderInfo"),
            });
            gd.IpnUrl = Lay(b, "ipnUrl");
            gd.ReturnUrl = Lay(b, "redirectUrl");

            return Ok(new
            {
                partnerCode = Lay(b, "partnerCode"),
                orderId = gd.MaDon,
                requestId = Lay(b, "requestId"),
                amount = gd.AmountVnd,
                resultCode = 0,
                message = "Thanh cong.",
                payUrl = _sandbox.PublicUrl.TrimEnd('/') + "/payments/sandbox/momo/pay/" + gd.MaDon,
            });
        }

        /// <summary>POST /payments/sandbox/momo/pay/{orderId}?result=success|cancel — nguoi dung xac nhan tren vi.</summary>
        [HttpPost("momo/pay/{orderId}")]
        public async Task<IActionResult> MoMoTra(string orderId, [FromQuery] string? result, [FromQuery] bool? sendIpn, CancellationToken ct)
        {
            GiaoDichGiaLap? gd;
            if (!_env.IsDevelopment() || !_kho.MoMo.TryGetValue(orderId, out gd))
            {
                return NotFound();
            }

            bool tra = !string.Equals(result, "cancel", StringComparison.OrdinalIgnoreCase);
            gd.TrangThai = tra ? "success" : "failed";
            gd.MaGiaoDich = tra ? RandomSo(10) : string.Empty;

            Dictionary<string, string> p = new Dictionary<string, string>(StringComparer.Ordinal);
            p["partnerCode"] = _momo.PartnerCode;
            p["orderId"] = gd.MaDon;
            p["requestId"] = Guid.NewGuid().ToString("N");
            p["amount"] = gd.AmountVnd.ToString(CultureInfo.InvariantCulture);
            p["orderInfo"] = gd.OrderInfo;
            p["orderType"] = "momo_wallet";
            p["transId"] = tra ? gd.MaGiaoDich : "0";
            p["resultCode"] = tra ? "0" : "1006";
            p["message"] = tra ? "Successful." : "Transaction denied by user.";
            p["payType"] = "qr";
            p["responseTime"] = _clock.GetUtcNow().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
            p["extraData"] = string.Empty;
            p["signature"] = MoMoKy.Hmac(_momo.SecretKey, MoMoKy.ChuoiKyIpn(_momo.AccessKey, p));

            int? maIpn = null;
            if (sendIpn != false && gd.IpnUrl.Length > 0)
            {
                // MoMo gui so (amount, transId, resultCode, responseTime) dang SO trong JSON.
                Dictionary<string, object> json = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, string> kv in p)
                {
                    long so;
                    bool laSo = (kv.Key == "amount" || kv.Key == "transId" || kv.Key == "resultCode" || kv.Key == "responseTime")
                        && long.TryParse(kv.Value, NumberStyles.None, CultureInfo.InvariantCulture, out so);
                    json[kv.Key] = laSo ? long.Parse(kv.Value, CultureInfo.InvariantCulture) : kv.Value;
                }

                HttpClient c = _http.CreateClient();
                using (HttpResponseMessage r = await c.PostAsJsonAsync(gd.IpnUrl, json, ct))
                {
                    maIpn = (int)r.StatusCode;
                }
            }

            return Ok(new { redirect = gd.ReturnUrl + "?orderId=" + gd.MaDon + "&resultCode=" + p["resultCode"], ipnStatus = maIpn });
        }

        /// <summary>POST /payments/sandbox/momo/v2/gateway/api/query</summary>
        [HttpPost("momo/v2/gateway/api/query")]
        public async Task<IActionResult> MoMoTraCuu(CancellationToken ct)
        {
            if (!_env.IsDevelopment() || !_momo.DuCauHinh)
            {
                return NotFound();
            }

            Dictionary<string, string>? b = WebhooksController.DocJsonPhang(await DocBodyAsync(ct));
            if (b == null)
            {
                return BadRequest();
            }

            string mongDoi = MoMoKy.Hmac(_momo.SecretKey, MoMoKy.ChuoiKyTraCuu(_momo.AccessKey, Lay(b, "orderId"), Lay(b, "partnerCode"), Lay(b, "requestId")));
            if (!MoMoKy.KhopChuKy(mongDoi, Lay(b, "signature")))
            {
                return Ok(new { orderId = Lay(b, "orderId"), resultCode = 11, message = "Access denied" });
            }

            GiaoDichGiaLap? gd;
            if (!_kho.MoMo.TryGetValue(Lay(b, "orderId"), out gd))
            {
                return Ok(new { orderId = Lay(b, "orderId"), resultCode = 42, message = "Invalid orderId or orderId is not found." });
            }

            int ma = gd.TrangThai == "success" ? 0 : (gd.TrangThai == "failed" ? 1006 : 1000);
            return Ok(new
            {
                partnerCode = _momo.PartnerCode,
                orderId = gd.MaDon,
                requestId = Lay(b, "requestId"),
                amount = gd.AmountVnd,
                transId = gd.MaGiaoDich.Length > 0 ? long.Parse(gd.MaGiaoDich, CultureInfo.InvariantCulture) : 0,
                resultCode = ma,
                message = ma == 0 ? "Successful." : "Pending / failed",
            });
        }

        // =====================================================================

        private Dictionary<string, string> Query()
        {
            Dictionary<string, string> q = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, StringValues> kv in Request.Query)
            {
                q[kv.Key] = kv.Value.ToString();
            }

            return q;
        }

        private async Task<string> DocBodyAsync(CancellationToken ct)
        {
            using (StreamReader r = new StreamReader(Request.Body, Encoding.UTF8))
            {
                return await r.ReadToEndAsync(ct);
            }
        }

        private static string Lay(Dictionary<string, string> d, string k)
        {
            string? v;
            return d.TryGetValue(k, out v) && v != null ? v : string.Empty;
        }

        private static string RandomSo(int doDai)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append((char)('1' + System.Security.Cryptography.RandomNumberGenerator.GetInt32(9)));
            for (int i = 1; i < doDai; i++)
            {
                sb.Append((char)('0' + System.Security.Cryptography.RandomNumberGenerator.GetInt32(10)));
            }

            return sb.ToString();
        }
    }
}
