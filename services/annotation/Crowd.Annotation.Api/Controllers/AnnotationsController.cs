using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Annotation.Api.Dtos;
using Crowd.Annotation.Api.Services;
using Crowd.Annotation.Domain.Annotations;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crowd.Annotation.Api.Controllers
{
    /// <summary>
    /// Moi endpoint nam duoi /annotations — gateway chi can MOT route.
    ///
    /// Du an lam trung tam (docs 3.18): KHONG con duyet / tu choi / khieu nai tung nhan. annotation-svc chi
    /// LUU nhan theo bai lam va tra ket qua / file xuat.
    ///
    /// Quyen:
    ///   - Xem nhan, ket qua, xuat file cua du an: chu du an (ban sao thanh vien) hoac admin.
    ///   - Lich su, nhan cua toi: labeler, CHI nhan cua chinh minh.
    /// </summary>
    [ApiController]
    [Route("annotations")]
    [Authorize]
    public sealed class AnnotationsController : ControllerBase
    {
        private readonly AnnotationService _service;

        public AnnotationsController(AnnotationService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            _service = service;
        }

        // ---- Doanh nghiep ----

        /// <summary>GET /annotations/projects/{projectId}?status=accepted&amp;attemptId= — nhan cua du an.</summary>
        [HttpGet("projects/{projectId:guid}")]
        public async Task<IActionResult> DanhSachDuAn(
            Guid projectId,
            [FromQuery] AnnotationStatus? status,
            [FromQuery] Guid? attemptId,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            CancellationToken ct)
        {
            return Ok(await _service.DanhSachDuAnAsync(projectId, status, attemptId, page, pageSize, ChuDuAn(), ct));
        }

        /// <summary>GET /annotations/{id}/history — vong doi mot nhan.</summary>
        [HttpGet("{id:guid}/history")]
        public async Task<IActionResult> LichSu(Guid id, CancellationToken ct)
        {
            return Ok(await _service.LichSuAsync(id, ChuDuAn(), ct));
        }

        /// <summary>GET /annotations/projects/{projectId}/results — ket qua gop tu nhan DA CHOT (FB-22).</summary>
        [HttpGet("projects/{projectId:guid}/results")]
        public async Task<IActionResult> KetQua(Guid projectId, CancellationToken ct)
        {
            ProjectResultsResponse kq = await _service.KetQuaAsync(projectId, ChuDuAn(), ct);
            // Bo nho dem ket qua gop: bao bao nhieu mau dung lai / tinh moi (do hieu nang, kiem thu).
            Response.Headers["X-Results-Cache"] = "hits=" + kq.CacheHits + "; misses=" + kq.CacheMisses;
            return Ok(kq);
        }

        /// <summary>GET /annotations/projects/{projectId}/distribution — phan bo nhan + canh bao lech (FB-24).</summary>
        [HttpGet("projects/{projectId:guid}/distribution")]
        public async Task<IActionResult> PhanBo(Guid projectId, CancellationToken ct)
        {
            return Ok(await _service.PhanBoAsync(projectId, ChuDuAn(), ct));
        }

        /// <summary>GET /annotations/projects/{projectId}/export?format=json|csv|coco|yolo|voc|... (FB-25)</summary>
        [HttpGet("projects/{projectId:guid}/export")]
        public async Task<IActionResult> Xuat(Guid projectId, [FromQuery] string? format, CancellationToken ct)
        {
            ExportFile f = await _service.XuatAsync(projectId, format, ChuDuAn(), ct);
            return File(f.NoiDung, f.ContentType, f.TenFile);
        }

        // ---- Labeler ----

        /// <summary>GET /annotations/mine?status=&amp;projectId= — nhan cua toi (FL-08).</summary>
        [HttpGet("mine")]
        [Authorize(Roles = CrowdRoles.Labeler)]
        public async Task<IActionResult> CuaToi(
            [FromQuery] AnnotationStatus? status, [FromQuery] Guid? projectId, [FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct)
        {
            return Ok(await _service.CuaToiAsync(status, projectId, page, pageSize, Caller.TuHttp(HttpContext, ActorRole.Labeler), ct));
        }

        private Caller ChuDuAn()
        {
            return Caller.TuHttp(HttpContext, ActorRole.Business);
        }
    }
}
