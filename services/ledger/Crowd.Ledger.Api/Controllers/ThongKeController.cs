using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Messaging;
using Crowd.Ledger.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crowd.Ledger.Api.Controllers
{
    /// <summary>Thong ke tien chi doc: thu nhap (FL-11), chi phi du an (FB-20).</summary>
    [ApiController]
    [Route("ledger")]
    [Authorize]
    public sealed class ThongKeController : ControllerBase
    {
        private readonly ThongKeService _thongKe;

        public ThongKeController(ThongKeService thongKe)
        {
            if (thongKe == null)
            {
                throw new ArgumentNullException(nameof(thongKe));
            }

            _thongKe = thongKe;
        }

        /// <summary>GET /ledger/me/earnings?from=2026-10-01&amp;to=2026-10-31&amp;groupBy=day|project</summary>
        [HttpGet("me/earnings")]
        [Authorize(Roles = CrowdRoles.Labeler + "," + CrowdRoles.Sharer)]
        public async Task<IActionResult> ThuNhap(
            [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? groupBy, CancellationToken ct)
        {
            return Ok(await _thongKe.ThuNhapAsync(Caller.TuHttp(HttpContext, ActorRole.Labeler), from, to, groupBy, ct));
        }

        /// <summary>GET /ledger/projects/{projectId}/cost — chu du an hoac admin.</summary>
        [HttpGet("projects/{projectId:guid}/cost")]
        [Authorize(Roles = CrowdRoles.Business + "," + CrowdRoles.Admin)]
        public async Task<IActionResult> ChiPhi(Guid projectId, CancellationToken ct)
        {
            return Ok(await _thongKe.ChiPhiDuAnAsync(projectId, Caller.TuHttp(HttpContext, ActorRole.Business), ct));
        }
    }
}
