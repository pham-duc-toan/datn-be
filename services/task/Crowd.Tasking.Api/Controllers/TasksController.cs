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
        /// <summary>Ma thiet bi frontend tu sinh va gui kem (tuy chon) — chong nhieu tai khoan (FM-07).</summary>
        private const string HeaderThietBi = "X-Device-Id";

        private readonly LeaseService _service;
        private readonly BaoLoiService _baoLoi;

        public TasksController(LeaseService service, BaoLoiService baoLoi)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            if (baoLoi == null)
            {
                throw new ArgumentNullException(nameof(baoLoi));
            }

            _service = service;
            _baoLoi = baoLoi;
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
            return Ok(await _service.NopAsync(
                assignmentId, body == null ? null : body.Payload, IpKhach(), ThietBi(), Labeler(), ct));
        }

        /// <summary>PUT /tasks/assignments/{id}/draft — luu nhap (FL-05), body {"payload": {...}} chua can hop le.</summary>
        [HttpPut("assignments/{assignmentId:guid}/draft")]
        [Authorize(Roles = CrowdRoles.Labeler)]
        public async Task<IActionResult> LuuNhap(Guid assignmentId, [FromBody] DraftRequest body, CancellationToken ct)
        {
            return Ok(await _service.LuuNhapAsync(assignmentId, body == null ? null : body.Payload, Labeler(), ct));
        }

        /// <summary>POST /tasks/assignments/{id}/report {"reason","note"} — bao task loi (FL-07), tra task lai.</summary>
        [HttpPost("assignments/{assignmentId:guid}/report")]
        [Authorize(Roles = CrowdRoles.Labeler)]
        public async Task<IActionResult> BaoLoi(Guid assignmentId, [FromBody] ReportTaskRequest body, CancellationToken ct)
        {
            return Ok(await _baoLoi.BaoLoiAsync(assignmentId, body, Labeler(), ct));
        }

        /// <summary>GET /tasks/projects/{projectId}/reports?status=open|dismissed|excluded|all — chu du an.</summary>
        [HttpGet("projects/{projectId:guid}/reports")]
        public async Task<IActionResult> DanhSachBaoLoi(Guid projectId, [FromQuery] string? status, CancellationToken ct)
        {
            return Ok(await _baoLoi.DanhSachAsync(projectId, status, Caller.TuHttp(HttpContext, ActorRole.Business), ct));
        }

        /// <summary>POST /tasks/reports/{id}/resolve {"action": "dismiss|exclude"} — chu du an.</summary>
        [HttpPost("reports/{reportId:guid}/resolve")]
        public async Task<IActionResult> XuLyBaoLoi(Guid reportId, [FromBody] ResolveReportRequest body, CancellationToken ct)
        {
            return Ok(await _baoLoi.XuLyAsync(reportId, body, Caller.TuHttp(HttpContext, ActorRole.Business), ct));
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

        /// <summary>IP that cua labeler (UseForwardedHeaders da doc X-Forwarded-For tu gateway tin cay).</summary>
        private string? IpKhach()
        {
            return HttpContext.Connection.RemoteIpAddress == null ? null : HttpContext.Connection.RemoteIpAddress.ToString();
        }

        private string? ThietBi()
        {
            string? v = Request.Headers[HeaderThietBi];
            if (string.IsNullOrWhiteSpace(v))
            {
                return null;
            }

            v = v.Trim();
            return v.Length > 100 ? v.Substring(0, 100) : v;
        }
    }
}
