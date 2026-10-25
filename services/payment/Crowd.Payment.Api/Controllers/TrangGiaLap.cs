using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Crowd.Payment.Api.Controllers
{
    /// <summary>
    /// Trang HTML cua cac cong thanh toan GIA LAP (chi Development) — nguoi dung mo bang trinh duyet tu
    /// checkoutUrl, bam "Thanh toan" / "Huy". Truoc day cac URL nay chi la API (POST / JSON) nen mo bang trinh
    /// duyet ra 404 / 405 / JSON tho.
    ///
    /// Nut bam goi dung endpoint POST gia lap da co; endpoint tra { redirect } thi chuyen ve trang ket qua cua
    /// frontend, khong thi bao xong va tu dong tab (cong sandbox mo o tab moi, frontend tu hoi trang thai).
    /// </summary>
    public static class TrangGiaLap
    {
        public static ContentResult Tao(string congThanhToan, string mauChu, long soTienVnd, string moTa, string urlThanhToan, string? urlHuy)
        {
            string tien = soTienVnd.ToString("N0", CultureInfo.GetCultureInfo("vi-VN")) + " đ";
            string nutHuy = urlHuy == null
                ? "<button class=\"phu\" onclick=\"huy()\">Huỷ</button>"
                : "<button class=\"phu\" onclick=\"bam('" + WebUtility.HtmlEncode(urlHuy) + "')\">Huỷ</button>";

            string html = "<!doctype html><html lang=\"vi\"><head><meta charset=\"utf-8\">"
                + "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
                + "<title>" + WebUtility.HtmlEncode(congThanhToan) + " (thử nghiệm)</title>"
                + "<style>body{font-family:system-ui,sans-serif;background:#f4f4f5;margin:0;display:flex;min-height:100vh;align-items:center;justify-content:center}"
                + ".the{background:#fff;border-radius:12px;padding:28px;max-width:380px;width:calc(100% - 32px);box-shadow:0 4px 24px #0001}"
                + "h1{font-size:18px;margin:0 0 4px;color:" + mauChu + "}.nho{color:#71717a;font-size:13px;margin:0 0 20px}"
                + ".tien{font-size:28px;font-weight:700;margin:8px 0 4px}button{width:100%;padding:12px;border-radius:8px;border:0;font-size:15px;cursor:pointer;margin-top:10px}"
                + ".chinh{background:" + mauChu + ";color:#fff}.phu{background:#e4e4e7}#tb{margin-top:14px;font-size:14px}</style></head><body>"
                + "<div class=\"the\"><h1>" + WebUtility.HtmlEncode(congThanhToan) + "</h1>"
                + "<p class=\"nho\">Cổng thanh toán THỬ NGHIỆM — không trừ tiền thật.</p>"
                + "<div class=\"nho\">" + WebUtility.HtmlEncode(moTa) + "</div>"
                + "<div class=\"tien\">" + WebUtility.HtmlEncode(tien) + "</div>"
                + "<button class=\"chinh\" onclick=\"bam('" + WebUtility.HtmlEncode(urlThanhToan) + "')\">Thanh toán</button>"
                + nutHuy
                + "<div id=\"tb\"></div></div>"
                + "<script>"
                + "function xong(t){document.getElementById('tb').textContent=t;document.querySelectorAll('button').forEach(function(b){b.disabled=true});setTimeout(function(){window.close()},1500)}"
                + "function huy(){xong('Đã huỷ. Bạn có thể đóng tab này.')}"
                + "async function bam(u){document.getElementById('tb').textContent='Đang xử lý…';"
                + "try{var r=await fetch(u,{method:'POST'});var j=await r.json().catch(function(){return{}});"
                + "if(!r.ok){document.getElementById('tb').textContent='Lỗi '+r.status;return}"
                + "if(j.redirect){location.href=j.redirect}else{xong('Đã thanh toán. Quay lại tab trước — số dư sẽ tự cập nhật.')}}"
                + "catch(e){document.getElementById('tb').textContent='Không gọi được cổng thử nghiệm: '+e}}"
                + "</script></body></html>";

            ContentResult kq = new ContentResult();
            kq.Content = html;
            kq.ContentType = "text/html; charset=utf-8";
            kq.StatusCode = StatusCodes.Status200OK;
            return kq;
        }

        /// <summary>Trinh duyet (Accept co text/html) — E2E / client API van nhan JSON nhu cu.</summary>
        public static bool LaTrinhDuyet(HttpRequest request)
        {
            string accept = request.Headers.Accept.ToString();
            return accept.Contains("text/html", System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
