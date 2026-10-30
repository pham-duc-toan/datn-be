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
    /// <summary>Quan ly danh muc the chu de (linh vuc / ngon ngu). Chi admin.</summary>
    [ApiController]
    [Route("admin/tags")]
    [Authorize(Roles = CrowdRoles.Admin)]
    public sealed class AdminTagsController : ControllerBase
    {
        private readonly TheChuDeService _the;

        public AdminTagsController(TheChuDeService the)
        {
            if (the == null)
            {
                throw new ArgumentNullException(nameof(the));
            }

            _the = the;
        }

        /// <summary>GET /admin/tags?kind=domain|language</summary>
        [HttpGet("")]
        public async Task<IActionResult> DanhSach([FromQuery] string? kind, CancellationToken ct)
        {
            return Ok(await _the.DanhSachAsync(kind, ct));
        }

        /// <summary>POST /admin/tags { kind, name, code? }</summary>
        [HttpPost("")]
        public async Task<IActionResult> Tao([FromBody] CreateTagRequest body, CancellationToken ct)
        {
            TagResponse t = await _the.TaoAsync(body, AdminId(), ct);
            return Created("/tags/" + t.Id, t);
        }

        /// <summary>PUT /admin/tags/{id} { name } — doi ten hien thi, ma giu nguyen.</summary>
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> DoiTen(Guid id, [FromBody] RenameTagRequest body, CancellationToken ct)
        {
            return Ok(await _the.DoiTenAsync(id, body, AdminId(), ct));
        }

        [HttpPost("{id:guid}/hide")]
        public async Task<IActionResult> An(Guid id, CancellationToken ct)
        {
            return Ok(await _the.DatAnAsync(id, true, AdminId(), ct));
        }

        [HttpPost("{id:guid}/show")]
        public async Task<IActionResult> Hien(Guid id, CancellationToken ct)
        {
            return Ok(await _the.DatAnAsync(id, false, AdminId(), ct));
        }

        /// <summary>POST /admin/tags/{id}/merge { targetId } — gop vao the khac cung loai (khong hoan tac).</summary>
        [HttpPost("{id:guid}/merge")]
        public async Task<IActionResult> Gop(Guid id, [FromBody] MergeTagRequest body, CancellationToken ct)
        {
            Caller caller = Caller.TuHttp(HttpContext, ActorRole.Admin);
            return Ok(await _the.GopAsync(id, body, caller.LayUserId(), caller.CorrelationId, ct));
        }

        private Guid AdminId()
        {
            return Caller.TuHttp(HttpContext, ActorRole.Admin).LayUserId();
        }
    }

    /// <summary>Danh muc the cho MOI nguoi dang nhap — o chon linh vuc / ngon ngu cua ho so va du an.</summary>
    [ApiController]
    [Route("tags")]
    [Authorize]
    public sealed class TagsController : ControllerBase
    {
        private readonly TheChuDeService _the;

        public TagsController(TheChuDeService the)
        {
            if (the == null)
            {
                throw new ArgumentNullException(nameof(the));
            }

            _the = the;
        }

        /// <summary>GET /tags?kind= — ca the an / da gop (de hien ten the da gan); o chon chi lay the con dung.</summary>
        [HttpGet("")]
        public async Task<IActionResult> DanhSach([FromQuery] string? kind, CancellationToken ct)
        {
            return Ok(await _the.DanhSachAsync(kind, ct));
        }
    }
}
