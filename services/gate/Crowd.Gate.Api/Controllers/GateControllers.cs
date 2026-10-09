using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Security;
using Crowd.Gate.Api.Dtos;
using Crowd.Gate.Api.Helpers;
using Crowd.Gate.Api.Services;
using Crowd.Gate.Infrastructure.ClickHouse;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Crowd.Gate.Api.Controllers
{
    /// <summary>Trang vuot link — cong khai, khach vang lai khong can dang nhap.</summary>
    [ApiController]
    [AllowAnonymous]
    public sealed class GatePublicController : ControllerBase
    {
        private readonly GateService _gate;
        private readonly PrivacyOptions _privacy;
        private readonly TimeProvider _clock;

        public GatePublicController(GateService gate, IOptions<PrivacyOptions> privacy, TimeProvider clock)
        {
            if (gate == null)
            {
                throw new ArgumentNullException(nameof(gate));
            }

            if (privacy == null)
            {
                throw new ArgumentNullException(nameof(privacy));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _gate = gate;
            _privacy = privacy.Value;
            _clock = clock;
        }

        /// <summary>GET /g/{code} — co can mat khau khong, dem nguoc bao lau, khoa Turnstile.</summary>
        [HttpGet("g/{code}")]
        public IActionResult Trang(string code)
        {
            return Ok(_gate.Trang(code));
        }

        /// <summary>POST /g/{code}/sessions {"turnstileToken","password"} — phat bo cau hoi.</summary>
        [HttpPost("g/{code}/sessions")]
        public async Task<IActionResult> TaoPhien(string code, [FromBody] CreateSessionRequest? body, CancellationToken ct)
        {
            return Ok(await _gate.TaoPhienAsync(code, body, Khach(), ct));
        }

        /// <summary>POST /g/sessions/{id}/submit {"answers":{sampleId: nhan}} — dat: redirectUrl; truot: newSession.</summary>
        [HttpPost("g/sessions/{id:guid}/submit")]
        public async Task<IActionResult> Nop(Guid id, [FromBody] SubmitRequest? body, CancellationToken ct)
        {
            return Ok(await _gate.NopAsync(id, body, Khach(), ct));
        }

        /// <summary>GET /go/{code}?t=token — dung token MOT lan, 302 toi link dich.</summary>
        [HttpGet("go/{code}")]
        public async Task<IActionResult> Mo(string code, [FromQuery] string? t)
        {
            string dich = await _gate.MoLinkAsync(code, t);
            return Redirect(dich);
        }

        private KhachVangLai Khach()
        {
            string ip = HttpContext.Connection.RemoteIpAddress == null ? "unknown" : HttpContext.Connection.RemoteIpAddress.ToString();

            string nguon = string.Empty;
            Uri? referer;
            string? tho = Request.Headers.Referer;
            if (tho != null && Uri.TryCreate(tho, UriKind.Absolute, out referer))
            {
                nguon = referer.IdnHost.ToLowerInvariant();
            }

            Guid? userId = null;
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                userId = CrowdClaims.GetUserId(User);
            }

            return new KhachVangLai
            {
                Ip = ip,
                IpHash = BamIp.Bam(ip, _privacy.IpSalt),
                IpHashNgay = BamIp.BamTheoNgay(ip, _privacy.IpSalt, _clock.GetUtcNow()),
                ReferrerHost = nguon.Length > 253 ? nguon.Substring(0, 253) : nguon,
                UserId = userId,
            };
        }
    }

    /// <summary>
    /// Thong ke cho nguoi chia se (FS-07), doc tu ClickHouse. Doanh thu la UOC TINH theo
    /// luot hop le — so chinh thuc nam o vi (ledger), vi ledger co the tu choi chi khi du an
    /// het ngan sach dung luc.
    /// </summary>
    [ApiController]
    [Route("gate/stats/me")]
    [Authorize(Roles = CrowdRoles.Sharer)]
    public sealed class GateStatsController : ControllerBase
    {
        private const int SoNgayToiDa = 366;

        private readonly ClickStore _clicks;
        private readonly TimeProvider _clock;

        public GateStatsController(ClickStore clicks, TimeProvider clock)
        {
            if (clicks == null)
            {
                throw new ArgumentNullException(nameof(clicks));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _clicks = clicks;
            _clock = clock;
        }

        /// <summary>GET /gate/stats/me/daily?from=2026-10-01&amp;to=2026-10-31 (mac dinh 30 ngay gan nhat).</summary>
        [HttpGet("daily")]
        public async Task<IActionResult> TheoNgay([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        {
            DateOnly[] k = Khoang(from, to);
            List<ThongKeNgay> ds = await _clicks.TheoNgayAsync(CrowdClaims.GetUserId(User), k[0], k[1], ct);
            return Ok(ds.Select(x => new DailyStatResponse
            {
                Date = x.Ngay,
                Views = x.LuotXem,
                Submits = x.LuotNop,
                PaidClicks = x.LuotTinhTien,
                EstimatedRevenueVnd = x.DoanhThuVnd,
            }).ToList());
        }

        /// <summary>GET /gate/stats/me/top-links — Top Link theo doanh thu.</summary>
        [HttpGet("top-links")]
        public Task<IActionResult> TopLink([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int? limit, CancellationToken ct)
        {
            return NhomAsync("link", from, to, limit, ct);
        }

        /// <summary>GET /gate/stats/me/sources — nguon traffic (ten mien referrer).</summary>
        [HttpGet("sources")]
        public Task<IActionResult> Nguon([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int? limit, CancellationToken ct)
        {
            return NhomAsync("referrer", from, to, limit, ct);
        }

        /// <summary>GET /gate/stats/me/outcomes — luot nop theo ket cuc (tinhTien, truotCauVang, trungIp, tuVuot...).</summary>
        [HttpGet("outcomes")]
        public Task<IActionResult> KetCuc([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        {
            return NhomAsync("outcome", from, to, 20, ct);
        }

        private async Task<IActionResult> NhomAsync(string nhom, DateOnly? from, DateOnly? to, int? limit, CancellationToken ct)
        {
            DateOnly[] k = Khoang(from, to);
            int n = limit.HasValue && limit.Value > 0 && limit.Value <= 100 ? limit.Value : 10;
            List<ThongKeNhom> ds = await _clicks.TheoNhomAsync(CrowdClaims.GetUserId(User), nhom, k[0], k[1], n, ct);
            return Ok(ds.Select(x => new GroupStatResponse
            {
                Key = x.Khoa,
                Views = x.LuotXem,
                PaidClicks = x.LuotTinhTien,
                EstimatedRevenueVnd = x.DoanhThuVnd,
            }).ToList());
        }

        private DateOnly[] Khoang(DateOnly? from, DateOnly? to)
        {
            DateOnly homNay = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
            DateOnly den = to.HasValue ? to.Value : homNay;
            DateOnly tu = from.HasValue ? from.Value : den.AddDays(-29);
            if (tu > den || den.DayNumber - tu.DayNumber > SoNgayToiDa)
            {
                throw new GateException(400, "khoang_ngay_khong_hop_le", "Khoang ngay khong hop le (toi da " + SoNgayToiDa + " ngay).");
            }

            return new DateOnly[] { tu, den };
        }
    }
}
