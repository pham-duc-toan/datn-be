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

        public UsersAdminController(TaiKhoanAdminService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            _service = service;
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
