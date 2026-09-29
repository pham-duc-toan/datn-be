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
    /// Quyen:
    ///   - Duyet/xem nhan cua du an: chu du an hoac reviewer (ban sao thanh vien), hoac admin.
    ///   - Lich su, khieu nai: labeler, CHI nhan cua chinh minh.
    ///   - Phan xu khieu nai: admin.
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

        // ---- Doanh nghiep / reviewer ----

        /// <summary>GET /annotations/projects/{projectId}?status=pendingReview</summary>
        [HttpGet("projects/{projectId:guid}")]
        public async Task<IActionResult> DanhSachDuAn(
            Guid projectId, [FromQuery] AnnotationStatus? status, [FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct)
        {
            return Ok(await _service.DanhSachDuAnAsync(projectId, status, page, pageSize, NguoiDuyet(), ct));
        }

        /// <summary>POST /annotations/{id}/approve (FB-21) — sinh ra tien cho labeler.</summary>
        [HttpPost("{id:guid}/approve")]
        public async Task<IActionResult> Duyet(Guid id, CancellationToken ct)
        {
            return Ok(await _service.DuyetAsync(id, NguoiDuyet(), ct));
        }

        /// <summary>POST /annotations/{id}/reject {reason} (FB-21)</summary>
        [HttpPost("{id:guid}/reject")]
        public async Task<IActionResult> TuChoi(Guid id, [FromBody] ReasonRequest? body, CancellationToken ct)
        {
            return Ok(await _service.TuChoiAsync(id, body == null ? null : body.Reason, NguoiDuyet(), ct));
        }

        /// <summary>GET /annotations/{id}/history — nhat ky vong doi.</summary>
        [HttpGet("{id:guid}/history")]
        public async Task<IActionResult> LichSu(Guid id, CancellationToken ct)
        {
            return Ok(await _service.LichSuAsync(id, NguoiDuyet(), ct));
        }

        /// <summary>GET /annotations/projects/{projectId}/results — nhan chot + tranh chap (FB-22).</summary>
        [HttpGet("projects/{projectId:guid}/results")]
        public async Task<IActionResult> KetQua(Guid projectId, CancellationToken ct)
        {
            return Ok(await _service.KetQuaAsync(projectId, NguoiDuyet(), ct));
        }

        /// <summary>GET /annotations/projects/{projectId}/export?format=json|csv (FB-25)</summary>
        [HttpGet("projects/{projectId:guid}/export")]
        public async Task<IActionResult> Xuat(Guid projectId, [FromQuery] string? format, CancellationToken ct)
        {
            ExportFile f = await _service.XuatAsync(projectId, format, NguoiDuyet(), ct);
            return File(f.NoiDung, f.ContentType, f.TenFile);
        }

        // ---- Labeler ----

        /// <summary>GET /annotations/mine — lich su + ti le duyet (FL-08).</summary>
        [HttpGet("mine")]
        [Authorize(Roles = CrowdRoles.Labeler)]
        public async Task<IActionResult> CuaToi(
            [FromQuery] AnnotationStatus? status, [FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct)
        {
            return Ok(await _service.CuaToiAsync(status, page, pageSize, Labeler(), ct));
        }

        /// <summary>POST /annotations/{id}/appeal {message} (FL-09)</summary>
        [HttpPost("{id:guid}/appeal")]
        [Authorize(Roles = CrowdRoles.Labeler)]
        public async Task<IActionResult> KhieuNai(Guid id, [FromBody] AppealRequest? body, CancellationToken ct)
        {
            return Ok(await _service.KhieuNaiAsync(id, body == null ? null : body.Message, Labeler(), ct));
        }

        // ---- Admin ----

        [HttpGet("appeals")]
        [Authorize(Roles = CrowdRoles.Admin)]
        public async Task<IActionResult> DanhSachKhieuNai([FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct)
        {
            return Ok(await _service.DanhSachKhieuNaiAsync(page, pageSize, ct));
        }

        /// <summary>POST /annotations/{id}/appeal/resolve {accept, note}</summary>
        [HttpPost("{id:guid}/appeal/resolve")]
        [Authorize(Roles = CrowdRoles.Admin)]
        public async Task<IActionResult> PhanXu(Guid id, [FromBody] ResolveAppealRequest body, CancellationToken ct)
        {
            return Ok(await _service.XuLyKhieuNaiAsync(id, body, Caller.TuHttp(HttpContext, ActorRole.Admin), ct));
        }

        private Caller NguoiDuyet()
        {
            return Caller.TuHttp(HttpContext, ActorRole.Business);
        }

        private Caller Labeler()
        {
            return Caller.TuHttp(HttpContext, ActorRole.Labeler);
        }
    }
}
