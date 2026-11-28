using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Project.Api.Dtos;
using Crowd.Project.Api.Services;
using Crowd.Project.Domain.Annotations;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crowd.Project.Api.Controllers
{
    /// <summary>
    /// Moi endpoint nam duoi /annotations — gateway chi can MOT route.
    ///
    /// Du an lam trung tam (docs 3.18): KHONG con duyet / tu choi / khieu nai tung nhan. Phan nhan chi
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

        /// <summary>
        /// GET /annotations/attempts/{attemptId}/history — vong doi cua bai chua nhan (nhan khong co id rieng, khoa
        /// la (du an, bai, cau); lich su ghi theo ca bai).
        /// </summary>
        [HttpGet("attempts/{attemptId:guid}/history")]
        public async Task<IActionResult> LichSu(Guid attemptId, CancellationToken ct)
        {
            return Ok(await _service.LichSuAsync(attemptId, ChuDuAn(), ct));
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

        /// <summary>
        /// PUT /annotations/projects/{projectId}/results/{sampleId}/override — {"data": {"cong cu": ket qua}, "note"}:
        /// chu du an sua ket qua cuoi cua mau (thay toan bo phan sua cu). Chi doi file xuat, khong doi cham bai / tien.
        /// </summary>
        [HttpPut("projects/{projectId:guid}/results/{sampleId:guid}/override")]
        public async Task<IActionResult> SuaKetQua(
            Guid projectId, Guid sampleId, [FromBody] SuaKetQuaRequest body, [FromServices] SuaKetQuaService sua, CancellationToken ct)
        {
            return Ok(await sua.SuaAsync(projectId, sampleId, body, ChuDuAn(), ct));
        }

        /// <summary>DELETE /annotations/projects/{projectId}/results/{sampleId}/override — bo phan sua, tro ve ket qua so dong.</summary>
        [HttpDelete("projects/{projectId:guid}/results/{sampleId:guid}/override")]
        public async Task<IActionResult> BoSuaKetQua(Guid projectId, Guid sampleId, [FromServices] SuaKetQuaService sua, CancellationToken ct)
        {
            await sua.BoSuaAsync(projectId, sampleId, ChuDuAn(), ct);
            return NoContent();
        }

        /// <summary>GET /annotations/projects/{projectId}/distribution — phan bo nhan + canh bao lech (FB-24).</summary>
        [HttpGet("projects/{projectId:guid}/distribution")]
        public async Task<IActionResult> PhanBo(Guid projectId, CancellationToken ct)
        {
            return Ok(await _service.PhanBoAsync(projectId, ChuDuAn(), ct));
        }

        // ---- Tao file ket qua CHAY NEN (FB-25, kien truc 3.41) ----

        /// <summary>
        /// POST /annotations/projects/{projectId}/exports — {"format": "json|csv|coco|yolo|voc|yolo-seg|yolo-obb|coco-keypoints|conll|mot"}.
        /// Kiem dinh dang ngay (400 neu khong hop du an), roi 202: file tao nen, theo doi o Location. Dang co lan cung
        /// dinh dang chua xong thi tra lai lan do.
        /// </summary>
        [HttpPost("projects/{projectId:guid}/exports")]
        public async Task<IActionResult> TaoFile(
            Guid projectId, [FromBody] TaoFileXuatRequest? body, [FromServices] ExportService xuat, CancellationToken ct)
        {
            ExportJobResponse r = await xuat.YeuCauAsync(projectId, body, ChuDuAn(), ct);
            return Accepted("/annotations/projects/" + projectId + "/exports/" + r.Id, r);
        }

        /// <summary>GET /annotations/projects/{projectId}/exports — cac lan tao file, moi nhat truoc (moi dinh dang giu lan moi nhat).</summary>
        [HttpGet("projects/{projectId:guid}/exports")]
        public async Task<IActionResult> DanhSachFile(Guid projectId, [FromServices] ExportService xuat, CancellationToken ct)
        {
            return Ok(await xuat.DanhSachAsync(projectId, ChuDuAn(), ct));
        }

        /// <summary>GET /annotations/projects/{projectId}/exports/{jobId} — trang thai: queued | processing | ready | failed.</summary>
        [HttpGet("projects/{projectId:guid}/exports/{jobId:guid}")]
        public async Task<IActionResult> TrangThaiFile(Guid projectId, Guid jobId, [FromServices] ExportService xuat, CancellationToken ct)
        {
            return Ok(await xuat.ChiTietAsync(projectId, jobId, ChuDuAn(), ct));
        }

        /// <summary>GET /annotations/projects/{projectId}/exports/{jobId}/file — tai file da tao (409 file_chua_san_sang neu chua xong).</summary>
        [HttpGet("projects/{projectId:guid}/exports/{jobId:guid}/file")]
        public async Task<IActionResult> TaiFile(Guid projectId, Guid jobId, [FromServices] ExportService xuat, CancellationToken ct)
        {
            FileXuatDangMo f = await xuat.MoFileAsync(projectId, jobId, ChuDuAn(), ct);
            Response.ContentLength = f.KichThuoc;

            // FileStreamResult tu Dispose luong khi gui xong.
            return File(f.Luong, f.ContentType, f.TenFile);
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
