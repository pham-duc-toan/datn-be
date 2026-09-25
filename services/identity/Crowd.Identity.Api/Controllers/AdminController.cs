using Crowd.BuildingBlocks.Auth.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crowd.Identity.Api.Controllers
{
    /// <summary>
    /// Kiem RBAC hoat dong: chi admin vao duoc. Labeler goi vao se nhan 403.
    ///
    /// [Authorize(Roles = ...)] doc claim "role" trong token — vi
    /// AddCrowdJwtAuthentication da dat RoleClaimType = "role".
    /// </summary>
    [ApiController]
    [Route("admin")]
    [Authorize(Roles = CrowdRoles.Admin)]
    public sealed class AdminController : ControllerBase
    {
        /// <summary>GET /admin/ping</summary>
        [HttpGet("ping")]
        public IActionResult Ping()
        {
            return Ok(new { ok = true });
        }
    }
}
