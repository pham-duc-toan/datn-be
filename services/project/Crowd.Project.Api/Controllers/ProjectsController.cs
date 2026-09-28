using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Messaging;
using Crowd.Project.Api.Dtos;
using Crowd.Project.Api.Services;
using Crowd.Project.Domain.Projects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crowd.Project.Api.Controllers
{
    /// <summary>
    /// Du an: tao, cau hinh, vong doi, xem.
    ///
    /// Hai lop quyen:
    ///   - CHIEU DOC (RBAC) bang [Authorize(Roles = ...)]: chi business tao du an.
    ///   - CHIEU NGANG (BOLA) trong ProjectAccessService: chi CHU du an nay sua duoc
    ///     du an nay. Business A khong sua duoc du an cua business B du cung vai tro.
    ///
    /// Moi route dung {id:guid}: "pending-approval" o AdminProjectsController khong
    /// bi hieu nham thanh mot id.
    /// </summary>
    [ApiController]
    [Route("projects")]
    [Authorize]
    public sealed class ProjectsController : ControllerBase
    {
        private readonly ProjectService _service;

        public ProjectsController(ProjectService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            _service = service;
        }

        /// <summary>POST /projects — tao du an Nhap (FB-10).</summary>
        [HttpPost]
        [Authorize(Roles = CrowdRoles.Business)]
        public async Task<IActionResult> Tao([FromBody] CreateProjectRequest body, CancellationToken ct)
        {
            ProjectResponse p = await _service.TaoAsync(body, ChuDuAn(), ct);
            return Created("/projects/" + p.Id, p);
        }

        /// <summary>
        /// GET /projects — danh sach du an XEM DUOC (FL-02). mine=true: chi du an
        /// minh la thanh vien.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> DanhSach(
            [FromQuery] ProjectStatus? status,
            [FromQuery] TaskType? taskType,
            [FromQuery] bool mine,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            CancellationToken ct)
        {
            Caller caller = Caller.TuHttp(HttpContext, ActorRole.Labeler);
            return Ok(await _service.DanhSachAsync(caller, status, taskType, mine, page, pageSize, ct));
        }

        /// <summary>GET /projects/{id}</summary>
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> ChiTiet(Guid id, CancellationToken ct)
        {
            return Ok(await _service.ChiTietAsync(id, Caller.TuHttp(HttpContext, ActorRole.Labeler), ct));
        }

        /// <summary>GET /projects/{id}/readiness — checklist truoc khi publish.</summary>
        [HttpGet("{id:guid}/readiness")]
        public async Task<IActionResult> SanSang(Guid id, CancellationToken ct)
        {
            return Ok(await _service.KiemTraSanSangAsync(id, ChuDuAn(), ct));
        }

        // ---- Cau hinh (chi khi Nhap) ----

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> CapNhat(Guid id, [FromBody] UpdateProjectInfoRequest body, CancellationToken ct)
        {
            return Ok(await _service.CapNhatThongTinAsync(id, body, ChuDuAn(), ct));
        }

        /// <summary>PUT /projects/{id}/label-schema (FB-12)</summary>
        [HttpPut("{id:guid}/label-schema")]
        public async Task<IActionResult> DatTapNhan(Guid id, [FromBody] LabelSchemaRequest body, CancellationToken ct)
        {
            return Ok(await _service.DatLabelSchemaAsync(id, body, ChuDuAn(), ct));
        }

        /// <summary>PUT /projects/{id}/guideline (FB-13)</summary>
        [HttpPut("{id:guid}/guideline")]
        public async Task<IActionResult> DatHuongDan(Guid id, [FromBody] GuidelineRequest body, CancellationToken ct)
        {
            return Ok(await _service.DatHuongDanAsync(id, body, ChuDuAn(), ct));
        }

        /// <summary>PUT /projects/{id}/pricing (FB-14)</summary>
        [HttpPut("{id:guid}/pricing")]
        public async Task<IActionResult> DatGia(Guid id, [FromBody] PricingRequest body, CancellationToken ct)
        {
            return Ok(await _service.DatCauHinhGiaAsync(id, body, ChuDuAn(), ct));
        }

        /// <summary>PUT /projects/{id}/channels (FB-17)</summary>
        [HttpPut("{id:guid}/channels")]
        public async Task<IActionResult> DatKenh(Guid id, [FromBody] ChannelsRequest body, CancellationToken ct)
        {
            return Ok(await _service.DatKenhAsync(id, body, ChuDuAn(), ct));
        }

        /// <summary>PUT /projects/{id}/eligibility (FB-15)</summary>
        [HttpPut("{id:guid}/eligibility")]
        public async Task<IActionResult> DatDieuKien(Guid id, [FromBody] EligibilityRequest body, CancellationToken ct)
        {
            return Ok(await _service.DatDieuKienAsync(id, body, ChuDuAn(), ct));
        }

        // ---- Vong doi (FB-18) ----

        /// <summary>POST /projects/{id}/publish — bat dau saga ky quy.</summary>
        [HttpPost("{id:guid}/publish")]
        public async Task<IActionResult> Publish(Guid id, CancellationToken ct)
        {
            return Ok(await _service.YeuCauPublishAsync(id, ChuDuAn(), ct));
        }

        [HttpPost("{id:guid}/pause")]
        public async Task<IActionResult> TamDung(Guid id, CancellationToken ct)
        {
            return Ok(await _service.TamDungAsync(id, ChuDuAn(), ct));
        }

        [HttpPost("{id:guid}/resume")]
        public async Task<IActionResult> TiepTuc(Guid id, CancellationToken ct)
        {
            return Ok(await _service.TiepTucAsync(id, ChuDuAn(), ct));
        }

        [HttpPost("{id:guid}/complete")]
        public async Task<IActionResult> HoanThanh(Guid id, CancellationToken ct)
        {
            return Ok(await _service.HoanThanhAsync(id, ChuDuAn(), ct));
        }

        [HttpPost("{id:guid}/cancel")]
        public async Task<IActionResult> Huy(Guid id, [FromBody] ReasonRequest? body, CancellationToken ct)
        {
            string? lyDo = body == null ? null : body.Reason;
            return Ok(await _service.HuyAsync(id, lyDo, ChuDuAn(), ct));
        }

        /// <summary>Moi thao tac sua du an deu voi tu cach doanh nghiep.</summary>
        private Caller ChuDuAn()
        {
            return Caller.TuHttp(HttpContext, ActorRole.Business);
        }
    }

    /// <summary>
    /// Duyet du an (FM-02). Tam dat o project-svc cho toi khi co admin-svc (P6);
    /// luc do admin-svc phat project.approved / project.rejected va project-svc
    /// nghe — cung goi dung ProjectService.DuyetAsync / TuChoiDuyetAsync.
    /// </summary>
    [ApiController]
    [Route("projects")]
    [Authorize(Roles = CrowdRoles.Admin)]
    public sealed class AdminProjectsController : ControllerBase
    {
        private readonly ProjectService _service;

        public AdminProjectsController(ProjectService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            _service = service;
        }

        /// <summary>GET /projects/pending-approval</summary>
        [HttpGet("pending-approval")]
        public async Task<IActionResult> ChoDuyet(CancellationToken ct)
        {
            return Ok(await _service.DanhSachChoDuyetAsync(Admin(), ct));
        }

        [HttpPost("{id:guid}/approve")]
        public async Task<IActionResult> Duyet(Guid id, CancellationToken ct)
        {
            return Ok(await _service.DuyetAsync(id, Admin(), ct));
        }

        [HttpPost("{id:guid}/reject")]
        public async Task<IActionResult> TuChoi(Guid id, [FromBody] ReasonRequest? body, CancellationToken ct)
        {
            string? lyDo = body == null ? null : body.Reason;
            return Ok(await _service.TuChoiDuyetAsync(id, lyDo, Admin(), ct));
        }

        private Caller Admin()
        {
            return Caller.TuHttp(HttpContext, ActorRole.Admin);
        }
    }
}
