using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Messaging;
using Crowd.Ledger.Api.Dtos;
using Crowd.Ledger.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crowd.Ledger.Api.Controllers
{
    /// <summary>Tranh chap nhan da duyet (FM-06): doanh nghiep mo, admin phan xu.</summary>
    [ApiController]
    [Route("ledger")]
    [Authorize]
    public sealed class TranhChapController : ControllerBase
    {
        private readonly DisputeService _tranhChap;

        public TranhChapController(DisputeService tranhChap)
        {
            if (tranhChap == null)
            {
                throw new ArgumentNullException(nameof(tranhChap));
            }

            _tranhChap = tranhChap;
        }

        /// <summary>POST /ledger/disputes { annotationId, reason } — doanh nghiep (chu du an).</summary>
        [HttpPost("disputes")]
        [Authorize(Roles = CrowdRoles.Business)]
        public async Task<IActionResult> Mo([FromBody] OpenDisputeRequest body, CancellationToken ct)
        {
            DisputeResponse d = await _tranhChap.MoAsync(body, Caller.TuHttp(HttpContext, ActorRole.Business), ct);
            return Created("/ledger/disputes/" + d.Id, d);
        }

        /// <summary>GET /ledger/disputes/mine?status= — tranh chap cua doanh nghiep dang goi.</summary>
        [HttpGet("disputes/mine")]
        [Authorize(Roles = CrowdRoles.Business)]
        public async Task<IActionResult> CuaToi([FromQuery] string? status, [FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct)
        {
            return Ok(await _tranhChap.CuaToiAsync(Caller.TuHttp(HttpContext, ActorRole.Business), status, page, pageSize, ct));
        }

        /// <summary>GET /ledger/admin/disputes?status=open — hang doi phan xu, cu nhat truoc.</summary>
        [HttpGet("admin/disputes")]
        [Authorize(Roles = CrowdRoles.Admin)]
        public async Task<IActionResult> HangDoi([FromQuery] string? status, [FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct)
        {
            return Ok(await _tranhChap.HangDoiAsync(status, page, pageSize, ct));
        }

        /// <summary>POST /ledger/admin/disputes/{id}/resolve { outcome: refundBusiness|payLabeler, note }</summary>
        [HttpPost("admin/disputes/{id:guid}/resolve")]
        [Authorize(Roles = CrowdRoles.Admin)]
        public async Task<IActionResult> PhanXu(Guid id, [FromBody] ResolveDisputeRequest body, CancellationToken ct)
        {
            return Ok(await _tranhChap.PhanXuAsync(id, body, Caller.TuHttp(HttpContext, ActorRole.Admin), ct));
        }
    }
}
