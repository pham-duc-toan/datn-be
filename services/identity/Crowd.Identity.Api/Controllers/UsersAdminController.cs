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
    /// Quan tri tai khoan (FM-01): danh sach, khoa / mo khoa. Gateway route "/users/{everything}".
    /// </summary>
    [ApiController]
    [Route("users/admin")]
    [Authorize(Roles = CrowdRoles.Admin)]
    public sealed class UsersAdminController : ControllerBase
    {
        private readonly TaiKhoanAdminService _service;
        private readonly HaiLopService _haiLop;
        private readonly XacMinhDanhTinhService _xacMinh;

        public UsersAdminController(TaiKhoanAdminService service, HaiLopService haiLop, XacMinhDanhTinhService xacMinh)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            if (haiLop == null)
            {
                throw new ArgumentNullException(nameof(haiLop));
            }

            if (xacMinh == null)
            {
                throw new ArgumentNullException(nameof(xacMinh));
            }

            _service = service;
            _haiLop = haiLop;
            _xacMinh = xacMinh;
        }

        /// <summary>POST /users/admin/{userId}/2fa/reset — go 2FA cho nguoi mat ca dien thoai lan ma du phong.</summary>
        [HttpPost("{userId:guid}/2fa/reset")]
        public async Task<IActionResult> GoHaiLop(Guid userId, CancellationToken ct)
        {
            await _haiLop.AdminGoAsync(userId, Caller.TuHttp(HttpContext, ActorRole.Admin), ct);
            return NoContent();
        }

        /// <summary>GET /users/admin/identity-verifications?status=submitted&amp;page=&amp;pageSize=</summary>
        [HttpGet("identity-verifications")]
        public async Task<IActionResult> HangDoiXacMinh([FromQuery] string? status, [FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct)
        {
            return Ok(await _xacMinh.HangDoiAsync(status, page, pageSize, ct));
        }

        /// <summary>GET /users/admin/identity-verifications/{userId} — kem link xem anh giay to (song ngan).</summary>
        [HttpGet("identity-verifications/{userId:guid}")]
        public async Task<IActionResult> ChiTietXacMinh(Guid userId, CancellationToken ct)
        {
            return Ok(await _xacMinh.ChiTietAsync(userId, Caller.TuHttp(HttpContext, ActorRole.Admin), ct));
        }

        [HttpPost("identity-verifications/{userId:guid}/approve")]
        public async Task<IActionResult> DuyetXacMinh(Guid userId, CancellationToken ct)
        {
            return Ok(await _xacMinh.DuyetAsync(userId, Caller.TuHttp(HttpContext, ActorRole.Admin), ct));
        }

        [HttpPost("identity-verifications/{userId:guid}/reject")]
        public async Task<IActionResult> TuChoiXacMinh(Guid userId, [FromBody] KybRejectRequest body, CancellationToken ct)
        {
            return Ok(await _xacMinh.TuChoiAsync(userId, body == null ? null : body.Reason, Caller.TuHttp(HttpContext, ActorRole.Admin), ct));
        }

        /// <summary>GET /users/admin?search=&amp;role=&amp;status=&amp;page=&amp;pageSize=</summary>
        [HttpGet]
        public async Task<IActionResult> DanhSach(
            [FromQuery] string? search, [FromQuery] string? role, [FromQuery] string? status,
            [FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct)
        {
            return Ok(await _service.DanhSachAsync(search, role, status, page, pageSize, ct));
        }

        /// <summary>POST /users/admin/{userId}/block { "reason": "..." }</summary>
        [HttpPost("{userId:guid}/block")]
        public async Task<IActionResult> Khoa(Guid userId, [FromBody] BlockUserRequest body, CancellationToken ct)
        {
            return Ok(await _service.KhoaAsync(userId, body == null ? null : body.Reason, Caller.TuHttp(HttpContext, ActorRole.Admin), ct));
        }

        /// <summary>POST /users/admin/{userId}/unblock</summary>
        [HttpPost("{userId:guid}/unblock")]
        public async Task<IActionResult> MoKhoa(Guid userId, CancellationToken ct)
        {
            return Ok(await _service.MoKhoaAsync(userId, Caller.TuHttp(HttpContext, ActorRole.Admin), ct));
        }
    }
}
