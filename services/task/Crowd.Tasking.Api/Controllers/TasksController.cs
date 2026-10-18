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
    /// Moi endpoint cua task-svc nam duoi /tasks — gateway chi can MOT route "/tasks/{everything}", khong dung
    /// cham "/projects/*" cua project-svc.
    ///
    /// Du an lam trung tam (docs 3.18): labeler lam HET cac cau cua du an trong MOT bai va nop ca bai.
    /// </summary>
    [ApiController]
    [Route("tasks")]
    [Authorize]
    public sealed class TasksController : ControllerBase
    {
        /// <summary>Ma thiet bi frontend tu sinh va gui kem (tuy chon) — chong nhieu tai khoan (FM-07).</summary>
        private const string HeaderThietBi = "X-Device-Id";

        private readonly LamBaiService _service;
        private readonly BaoLoiService _baoLoi;
        private readonly DonDuAnDaDongService _don;

        public TasksController(LamBaiService service, BaoLoiService baoLoi, DonDuAnDaDongService don)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            if (baoLoi == null)
            {
                throw new ArgumentNullException(nameof(baoLoi));
            }

            if (don == null)
            {
                throw new ArgumentNullException(nameof(don));
            }

            _service = service;
            _baoLoi = baoLoi;
            _don = don;
        }

        // ---- Bai lam (labeler) ----

        /// <summary>
        /// POST /tasks/projects/{projectId}/attempt — bat dau lam bai (giu mot suat) hoac mo lai bai dang lam.
        /// Tra CA DE (cau that + cau vang tron, thu tu rieng) kem cau tra loi da luu.
        /// </summary>
        [HttpPost("projects/{projectId:guid}/attempt")]
        [Authorize(Roles = CrowdRoles.Labeler)]
        public async Task<IActionResult> BatDau(Guid projectId, CancellationToken ct)
        {
            return Ok(await _service.BatDauAsync(projectId, Labeler(), ct));
        }

        /// <summary>GET /tasks/attempts/{id} — mo lai bai (link xem file moi, song ngan).</summary>
        [HttpGet("attempts/{attemptId:guid}")]
        [Authorize(Roles = CrowdRoles.Labeler)]
        public async Task<IActionResult> Lay(Guid attemptId, CancellationToken ct)
        {
            return Ok(await _service.LayAsync(attemptId, Labeler(), ct));
        }

        /// <summary>PUT /tasks/attempts/{id}/answers/{sampleId} — luu nhap MOT cau, body {"data": {...} | null}.</summary>
        [HttpPut("attempts/{attemptId:guid}/answers/{sampleId:guid}")]
        [Authorize(Roles = CrowdRoles.Labeler)]
        public async Task<IActionResult> LuuCauTraLoi(Guid attemptId, Guid sampleId, [FromBody] SaveAnswerRequest body, CancellationToken ct)
        {
            return Ok(await _service.LuuCauTraLoiAsync(attemptId, sampleId, body == null ? null : body.Data, Labeler(), ct));
        }

        /// <summary>POST /tasks/attempts/{id}/submit — nop CA BAI. Cau sai dinh dang → 400 bai_chua_hop_le kem invalidAnswers.</summary>
        [HttpPost("attempts/{attemptId:guid}/submit")]
        [Authorize(Roles = CrowdRoles.Labeler)]
        public async Task<IActionResult> Nop(Guid attemptId, CancellationToken ct)
        {
            return Ok(await _service.NopAsync(attemptId, IpKhach(), ThietBi(), Labeler(), ct));
        }

        /// <summary>POST /tasks/attempts/{id}/abandon — bo bai, tra suat cho nguoi khac.</summary>
        [HttpPost("attempts/{attemptId:guid}/abandon")]
        [Authorize(Roles = CrowdRoles.Labeler)]
        public async Task<IActionResult> BoBai(Guid attemptId, CancellationToken ct)
        {
            await _service.BoBaiAsync(attemptId, Labeler(), ct);
            return NoContent();
        }

        /// <summary>POST /tasks/attempts/{id}/appeal {"message"} — bai TRUOT khieu nai ket qua ca bai (trong han).</summary>
        [HttpPost("attempts/{attemptId:guid}/appeal")]
        [Authorize(Roles = CrowdRoles.Labeler)]
        public async Task<IActionResult> KhieuNai(Guid attemptId, [FromBody] AppealRequest body, CancellationToken ct)
        {
            return Ok(await _service.KhieuNaiAsync(attemptId, body, Labeler(), ct));
        }

        /// <summary>GET /tasks/me/attempts — moi bai cua toi (ket qua, khieu nai).</summary>
        [HttpGet("me/attempts")]
        [Authorize(Roles = CrowdRoles.Labeler)]
        public async Task<IActionResult> BaiCuaToi(CancellationToken ct)
        {
            return Ok(await _service.BaiCuaToiAsync(Labeler(), ct));
        }

        /// <summary>GET /tasks/projects/{id}/slots — so suat con lai + bai cua chinh nguoi goi.</summary>
        [HttpGet("projects/{projectId:guid}/slots")]
        public async Task<IActionResult> Suat(Guid projectId, CancellationToken ct)
        {
            return Ok(await _service.SuatAsync(projectId, Labeler(), ct));
        }

        // ---- Bao loi cau hoi (FL-07) ----

        /// <summary>POST /tasks/attempts/{id}/reports {"sampleId","reason","note"} — bao mot cau trong bai bi loi.</summary>
        [HttpPost("attempts/{attemptId:guid}/reports")]
        [Authorize(Roles = CrowdRoles.Labeler)]
        public async Task<IActionResult> BaoLoi(Guid attemptId, [FromBody] ReportTaskRequest body, CancellationToken ct)
        {
            return Ok(await _baoLoi.BaoLoiAsync(attemptId, body, Labeler(), ct));
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

        // ---- Chu du an ----

        /// <summary>GET /tasks/projects/{projectId}/progress — so suat, bai dang lam / da nop, giai doan cham.</summary>
        [HttpGet("projects/{projectId:guid}/progress")]
        public async Task<IActionResult> TienDo(Guid projectId, CancellationToken ct)
        {
            return Ok(await _service.TienDoAsync(projectId, Caller.TuHttp(HttpContext, ActorRole.Business), ct));
        }

        /// <summary>GET /tasks/projects/{projectId}/attempts — moi bai cua du an (ket qua cham, khieu nai).</summary>
        [HttpGet("projects/{projectId:guid}/attempts")]
        public async Task<IActionResult> BaiCuaDuAn(Guid projectId, CancellationToken ct)
        {
            return Ok(await _service.BaiCuaDuAnAsync(projectId, Caller.TuHttp(HttpContext, ActorRole.Business), ct));
        }

        // ---- Admin ----

        /// <summary>GET /tasks/admin/appeals?state=open|accepted|rejected — hang doi khieu nai ca bai.</summary>
        [HttpGet("admin/appeals")]
        [Authorize(Roles = CrowdRoles.Admin)]
        public async Task<IActionResult> DanhSachKhieuNai([FromQuery] string? state, CancellationToken ct)
        {
            return Ok(await _service.DanhSachKhieuNaiAsync(state, ct));
        }

        /// <summary>POST /tasks/admin/attempts/{id}/appeal/resolve {"accept","note"} — phan xu khieu nai.</summary>
        [HttpPost("admin/attempts/{attemptId:guid}/appeal/resolve")]
        [Authorize(Roles = CrowdRoles.Admin)]
        public async Task<IActionResult> PhanXu(Guid attemptId, [FromBody] ResolveAppealRequest body, CancellationToken ct)
        {
            return Ok(await _service.PhanXuAsync(attemptId, body, Caller.TuHttp(HttpContext, ActorRole.Admin), ct));
        }

        /// <summary>
        /// POST /tasks/admin/projects/{projectId}/purge — don NGAY du lieu van hanh cua mot du an da dong (khong
        /// cho han giu). Bai lam va ket qua van giu.
        /// </summary>
        [HttpPost("admin/projects/{projectId:guid}/purge")]
        [Authorize(Roles = CrowdRoles.Admin)]
        public async Task<IActionResult> DonDuAn(Guid projectId, CancellationToken ct)
        {
            return Ok(await _don.DonAsync(projectId, ct));
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
