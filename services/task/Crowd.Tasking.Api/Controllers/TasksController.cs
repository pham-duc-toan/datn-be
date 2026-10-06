using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Messaging;
using Crowd.Tasking.Api.Dtos;
using Crowd.Tasking.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crowd.Tasking.Api.Controllers
{
    /// <summary>
    /// Moi endpoint cua task-svc nam duoi /tasks — gateway chi can MOT route
    /// "/tasks/{everything}", khong dung cham "/projects/*" cua project-svc.
    /// </summary>
    [ApiController]
    [Route("tasks")]
    [Authorize]
    public sealed class TasksController : ControllerBase
    {
        private readonly LeaseService _service;

        public TasksController(LeaseService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            _service = service;
        }

        /// <summary>
        /// POST /tasks/projects/{projectId}/next — nhan mot task (FL-06).
        /// 200 kem task, hoac 204 neu het task.
        /// </summary>
        [HttpPost("projects/{projectId:guid}/next")]
        [Authorize(Roles = CrowdRoles.Labeler)]
        public async Task<IActionResult> NhanTask(Guid projectId, CancellationToken ct)
        {
            LeaseResponse? lease = await _service.NhanTaskAsync(projectId, Labeler(), ct);

            if (lease == null)
            {
                return NoContent();
            }

            return Ok(lease);
        }

        /// <summary>GET /tasks/assignments/mine — cac task dang giu.</summary>
        [HttpGet("assignments/mine")]
        [Authorize(Roles = CrowdRoles.Labeler)]
        public async Task<IActionResult> DangGiu(CancellationToken ct)
        {
            return Ok(await _service.DangGiuAsync(Labeler(), ct));
        }

        /// <summary>POST /tasks/assignments/{id}/submit — body {"payload": {...}} theo loai nhan cua du an.</summary>
        [HttpPost("assignments/{assignmentId:guid}/submit")]
        [Authorize(Roles = CrowdRoles.Labeler)]
        public async Task<IActionResult> Nop(Guid assignmentId, [FromBody] SubmitRequest body, CancellationToken ct)
        {
            if (body == null)
            {
                return Ok(await _service.NopAsync(assignmentId, null, null, Labeler(), ct));
            }

            return Ok(await _service.NopAsync(assignmentId, body.Payload, body.SchemaVersion, Labeler(), ct));
        }

        /// <summary>POST /tasks/assignments/{id}/release — bo qua task (FL-05).</summary>
        [HttpPost("assignments/{assignmentId:guid}/release")]
        [Authorize(Roles = CrowdRoles.Labeler)]
        public async Task<IActionResult> BoQua(Guid assignmentId, CancellationToken ct)
        {
            await _service.BoQuaAsync(assignmentId, Labeler(), ct);
            return NoContent();
        }

        /// <summary>GET /tasks/projects/{projectId}/progress — chu du an xem tien do (FB-20).</summary>
        [HttpGet("projects/{projectId:guid}/progress")]
        public async Task<IActionResult> TienDo(Guid projectId, CancellationToken ct)
        {
            return Ok(await _service.TienDoAsync(projectId, Caller.TuHttp(HttpContext, ActorRole.Business), ct));
        }

        private Caller Labeler()
        {
            return Caller.TuHttp(HttpContext, ActorRole.Labeler);
        }
    }
}
