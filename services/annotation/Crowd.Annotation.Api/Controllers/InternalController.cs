using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Annotation.Api.Dtos;
using Crowd.Annotation.Api.Services;
using Crowd.BuildingBlocks.Auth.Internal;
using Microsoft.AspNetCore.Mvc;

namespace Crowd.Annotation.Api.Controllers
{
    /// <summary>
    /// Endpoint NOI BO (service goi service), khong qua gateway, can X-Internal-Key.
    /// </summary>
    [ApiController]
    [Route("internal")]
    [InternalApi]
    public sealed class InternalController : ControllerBase
    {
        private readonly DongSoService _dongSo;

        public InternalController(DongSoService dongSo)
        {
            if (dongSo == null)
            {
                throw new ArgumentNullException(nameof(dongSo));
            }

            _dongSo = dongSo;
        }

        /// <summary>
        /// POST /internal/projects/{id}/close — dong so truoc khi project-svc hoan thanh / huy
        /// du an. Luon 200: Closed cho biet dong duoc chua, Reasons + so lieu neu chua.
        /// Goi lai khi da dong → van Closed = true (idempotent).
        /// </summary>
        [HttpPost("projects/{projectId:guid}/close")]
        public async Task<IActionResult> DongSo(Guid projectId, [FromBody] CloseProjectRequest body, CancellationToken ct)
        {
            if (body == null)
            {
                return BadRequest();
            }

            return Ok(await _dongSo.DongSoAsync(projectId, body.SubmittedCount, ct));
        }
    }
}
