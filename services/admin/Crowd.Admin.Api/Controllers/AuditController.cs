using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Admin.Api.Dtos;
using Crowd.Admin.Api.Services;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crowd.Admin.Api.Controllers
{
    /// <summary>Nhat ky audit (FP-06), thong ke tong quan va thong bao toan he thong (FM-09). Chi admin.</summary>
    [ApiController]
    [Route("admin")]
    [Authorize(Roles = CrowdRoles.Admin)]
    public sealed class AuditController : ControllerBase
    {
        private readonly AuditService _audit;
        private readonly AnnouncementService _thongBao;

        public AuditController(AuditService audit, AnnouncementService thongBao)
        {
            if (audit == null)
            {
                throw new ArgumentNullException(nameof(audit));
            }

            if (thongBao == null)
            {
                throw new ArgumentNullException(nameof(thongBao));
            }

            _audit = audit;
            _thongBao = thongBao;
        }

        /// <summary>
        /// GET /admin/audit?actorId=&amp;eventType=&amp;correlationId=&amp;from=&amp;to=&amp;page=&amp;pageSize=
        /// eventType ket thuc bang "." la loc tien to (vd "project.").
        /// </summary>
        [HttpGet("audit")]
        public async Task<IActionResult> NhatKy(
            [FromQuery] Guid? actorId, [FromQuery] string? eventType, [FromQuery] Guid? correlationId,
            [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
            [FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct)
        {
            return Ok(await _audit.TraCuuAsync(actorId, eventType, correlationId, from, to, page, pageSize, ct));
        }

        /// <summary>GET /admin/stats/overview?from=2026-10-01&amp;to=2026-10-31 (ngay gio Viet Nam, mac dinh 30 ngay)</summary>
        [HttpGet("stats/overview")]
        public async Task<IActionResult> ThongKe([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        {
            return Ok(await _audit.TongQuanAsync(from, to, ct));
        }

        /// <summary>POST /admin/announcements { title, body, audience: all|business|labeler|sharer, sendEmail }</summary>
        [HttpPost("announcements")]
        public async Task<IActionResult> GuiThongBao([FromBody] CreateAnnouncementRequest body, CancellationToken ct)
        {
            Caller caller = Caller.TuHttp(HttpContext, ActorRole.Admin);
            AnnouncementResponse a = await _thongBao.GuiAsync(body, caller.LayUserId(), caller.CorrelationId, ct);
            return Created("/admin/announcements/" + a.Id, a);
        }

        /// <summary>GET /admin/announcements?limit=20 — moi nhat truoc.</summary>
        [HttpGet("announcements")]
        public async Task<IActionResult> DanhSachThongBao([FromQuery] int limit, CancellationToken ct)
        {
            return Ok(await _thongBao.DanhSachAsync(limit, ct));
        }
    }
}
