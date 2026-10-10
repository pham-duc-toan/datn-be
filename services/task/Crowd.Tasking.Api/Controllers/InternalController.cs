using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Internal;
using Crowd.Tasking.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Crowd.Tasking.Api.Controllers
{
    /// <summary>
    /// Endpoint NOI BO (service goi service), khong qua gateway, can X-Internal-Key.
    /// </summary>
    [ApiController]
    [Route("internal")]
    [InternalApi]
    public sealed class InternalController : ControllerBase
    {
        private readonly LeaseService _service;

        public InternalController(LeaseService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            _service = service;
        }

        /// <summary>GET /internal/projects/{id}/close-check — project-svc hoi truoc khi dong du an.</summary>
        [HttpGet("projects/{projectId:guid}/close-check")]
        public async Task<IActionResult> KiemDong(Guid projectId, CancellationToken ct)
        {
            return Ok(await _service.KiemDongDuAnAsync(projectId, ct));
        }
    }
}
