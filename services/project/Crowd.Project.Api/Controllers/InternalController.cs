using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Internal;
using Crowd.Project.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Crowd.Project.Api.Controllers
{
    /// <summary>
    /// Endpoint NOI BO (service goi service), khong qua gateway, can X-Internal-Key.
    /// </summary>
    [ApiController]
    [Route("internal")]
    [InternalApi]
    public sealed class InternalController : ControllerBase
    {
        private readonly ProjectService _projects;

        public InternalController(ProjectService projects)
        {
            if (projects == null)
            {
                throw new ArgumentNullException(nameof(projects));
            }

            _projects = projects;
        }

        /// <summary>
        /// GET /internal/projects/{id}/published-terms — dieu khoan luc publish (cung hinh dang payload
        /// project.published). Service giu ban sao (quality-svc) goi khi nhan event cua du an ma chua co
        /// ban sao (du an publish truoc khi service do ton tai). Chua publish → 404.
        /// </summary>
        [HttpGet("projects/{projectId:guid}/published-terms")]
        public async Task<IActionResult> DieuKhoan(Guid projectId, CancellationToken ct)
        {
            return Ok(await _projects.DieuKhoanDaPublishAsync(projectId, ct));
        }
    }
}
