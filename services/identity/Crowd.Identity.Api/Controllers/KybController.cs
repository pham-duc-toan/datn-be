using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Messaging;
using Crowd.Identity.Api.Dtos;
using Crowd.Identity.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crowd.Identity.Api.Controllers
{
    /// <summary>
    /// Xac minh doanh nghiep (KYB). Gateway route "/kyb/{everything}" → identity-svc.
    /// (Khong dat duoi /admin: gateway chuyen /admin/* sang admin-svc.)
    /// </summary>
    [ApiController]
    [Route("kyb")]
    [Authorize]
    public sealed class KybController : ControllerBase
    {
        private readonly KybService _service;

        public KybController(KybService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            _service = service;
        }

        // ---------------- doanh nghiep ----------------

        /// <summary>GET /kyb/me — ho so cua minh (chua co thi tra ban nhap rong).</summary>
        [HttpGet("me")]
        [Authorize(Roles = CrowdRoles.Business)]
        public async Task<IActionResult> CuaToi(CancellationToken ct)
        {
            return Ok(await _service.CuaToiAsync(DoanhNghiep(), ct));
        }

        /// <summary>PUT /kyb/me — ten, ma so thue, dia chi.</summary>
        [HttpPut("me")]
        [Authorize(Roles = CrowdRoles.Business)]
        public async Task<IActionResult> CapNhat([FromBody] UpdateKybRequest body, CancellationToken ct)
        {
            return Ok(await _service.CapNhatAsync(body, DoanhNghiep(), ct));
        }

        /// <summary>POST /kyb/me/license — link PUT de tai giay phep thang len kho file.</summary>
        [HttpPost("me/license")]
        [Authorize(Roles = CrowdRoles.Business)]
        public async Task<IActionResult> XinLinkGiayPhep([FromBody] LicenseUploadRequest body, CancellationToken ct)
        {
            return Ok(await _service.XinLinkTaiGiayPhepAsync(body, DoanhNghiep(), ct));
        }

        /// <summary>POST /kyb/me/submit — nop ho so cho admin duyet.</summary>
        [HttpPost("me/submit")]
        [Authorize(Roles = CrowdRoles.Business)]
        public async Task<IActionResult> Nop(CancellationToken ct)
        {
            return Ok(await _service.NopAsync(DoanhNghiep(), ct));
        }

        // ---------------- admin (FM-01) ----------------

        /// <summary>GET /kyb/admin?status=submitted&amp;page=&amp;pageSize= — hang doi duyet.</summary>
        [HttpGet("admin")]
        [Authorize(Roles = CrowdRoles.Admin)]
        public async Task<IActionResult> HangDoi([FromQuery] string? status, [FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct)
        {
            return Ok(await _service.HangDoiAsync(status, page, pageSize, ct));
        }

        /// <summary>GET /kyb/admin/{businessId} — chi tiet + link xem giay phep (song ngan).</summary>
        [HttpGet("admin/{businessId:guid}")]
        [Authorize(Roles = CrowdRoles.Admin)]
        public async Task<IActionResult> ChiTiet(Guid businessId, CancellationToken ct)
        {
            return Ok(await _service.ChiTietAsync(businessId, ct));
        }

        [HttpPost("admin/{businessId:guid}/approve")]
        [Authorize(Roles = CrowdRoles.Admin)]
        public async Task<IActionResult> Duyet(Guid businessId, CancellationToken ct)
        {
            return Ok(await _service.DuyetAsync(businessId, Caller.TuHttp(HttpContext, ActorRole.Admin), ct));
        }

        [HttpPost("admin/{businessId:guid}/reject")]
        [Authorize(Roles = CrowdRoles.Admin)]
        public async Task<IActionResult> TuChoi(Guid businessId, [FromBody] KybRejectRequest body, CancellationToken ct)
        {
            return Ok(await _service.TuChoiAsync(businessId, body == null ? null : body.Reason, Caller.TuHttp(HttpContext, ActorRole.Admin), ct));
        }

        private Caller DoanhNghiep()
        {
            return Caller.TuHttp(HttpContext, ActorRole.Business);
        }
    }
}
