using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Messaging;
using Crowd.Project.Api.Dtos;
using Crowd.Project.Api.Services;
using Crowd.Project.Domain.Members;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crowd.Project.Api.Controllers
{
    /// <summary>
    /// Bao ve du an (muc 2.9 noi dung nhay cam, FP-01 NDA, FP-05 xac minh danh tinh), nhan goi y va goi y
    /// du an (FL-02), loi moi cho.
    /// </summary>
    [ApiController]
    [Route("projects")]
    [Authorize]
    public sealed class BaoVeController : ControllerBase
    {
        private readonly BaoVeService _baoVe;
        private readonly ProjectService _projects;
        private readonly GoiYService _goiY;
        private readonly InvitationService _loiMoi;

        public BaoVeController(BaoVeService baoVe, ProjectService projects, GoiYService goiY, InvitationService loiMoi)
        {
            if (baoVe == null)
            {
                throw new ArgumentNullException(nameof(baoVe));
            }

            if (projects == null)
            {
                throw new ArgumentNullException(nameof(projects));
            }

            if (goiY == null)
            {
                throw new ArgumentNullException(nameof(goiY));
            }

            if (loiMoi == null)
            {
                throw new ArgumentNullException(nameof(loiMoi));
            }

            _baoVe = baoVe;
            _projects = projects;
            _goiY = goiY;
            _loiMoi = loiMoi;
        }

        /// <summary>PUT /projects/{id}/protection — canh bao noi dung, NDA, yeu cau xac minh (chi khi Nhap).</summary>
        [HttpPut("{projectId:guid}/protection")]
        [Authorize(Roles = CrowdRoles.Business + "," + CrowdRoles.Admin)]
        public async Task<IActionResult> CauHinh(Guid projectId, [FromBody] ProtectionRequest body, CancellationToken ct)
        {
            return Ok(await _baoVe.CauHinhAsync(projectId, body, Caller.TuHttp(HttpContext, ActorRole.Business), ct));
        }

        /// <summary>GET /projects/{id}/terms — van ban canh bao / NDA + trang thai dong y cua toi.</summary>
        [HttpGet("{projectId:guid}/terms")]
        public async Task<IActionResult> DieuKhoan(Guid projectId, CancellationToken ct)
        {
            return Ok(await _baoVe.DieuKhoanAsync(projectId, NguoiDung(), ct));
        }

        /// <summary>POST /projects/{id}/consent { acceptContentWarning, acceptNda, fullName }</summary>
        [HttpPost("{projectId:guid}/consent")]
        public async Task<IActionResult> DongY(Guid projectId, [FromBody] ConsentRequest body, CancellationToken ct)
        {
            string? ip = HttpContext.Connection.RemoteIpAddress == null ? null : HttpContext.Connection.RemoteIpAddress.ToString();
            string? ua = Request.Headers.UserAgent;
            return Ok(await _baoVe.DongYAsync(projectId, body, ip, ua, NguoiDung(), ct));
        }

        /// <summary>GET /projects/{id}/consents — chu du an xem ai da dong y / ky NDA.</summary>
        [HttpGet("{projectId:guid}/consents")]
        public async Task<IActionResult> DanhSachDongY(Guid projectId, CancellationToken ct)
        {
            return Ok(await _baoVe.DanhSachAsync(projectId, Caller.TuHttp(HttpContext, ActorRole.Business), ct));
        }

        /// <summary>PUT /projects/{id}/tags { domains, languages } — nhan de goi y du an.</summary>
        [HttpPut("{projectId:guid}/tags")]
        [Authorize(Roles = CrowdRoles.Business + "," + CrowdRoles.Admin)]
        public async Task<IActionResult> DatNhan(Guid projectId, [FromBody] TagsRequest body, CancellationToken ct)
        {
            return Ok(await _projects.DatNhanAsync(projectId, body, Caller.TuHttp(HttpContext, ActorRole.Business), ct));
        }

        /// <summary>PUT /projects/{id}/retention { deleteFilesAfterClose } — dong y xoa file goc khi du an dong qua han giu.</summary>
        [HttpPut("{projectId:guid}/retention")]
        [Authorize(Roles = CrowdRoles.Business + "," + CrowdRoles.Admin)]
        public async Task<IActionResult> DatLuuTru(Guid projectId, [FromBody] RetentionRequest body, CancellationToken ct)
        {
            return Ok(await _projects.DatLuuTruAsync(projectId, body, Caller.TuHttp(HttpContext, ActorRole.Business), ct));
        }

        /// <summary>GET /projects/recommended?limit=10 — goi y du an hop ho so ky nang (FL-02).</summary>
        [HttpGet("recommended")]
        [Authorize(Roles = CrowdRoles.Labeler)]
        public async Task<IActionResult> GoiY([FromQuery] int limit, CancellationToken ct)
        {
            return Ok(await _goiY.GoiYAsync(limit, Caller.TuHttp(HttpContext, ActorRole.Labeler), ct));
        }

        // ---------------- Loi moi cho ----------------

        /// <summary>GET /projects/invitations/mine</summary>
        [HttpGet("invitations/mine")]
        public async Task<IActionResult> LoiMoiCuaToi(CancellationToken ct)
        {
            return Ok(await _loiMoi.CuaToiAsync(NguoiDung(), ct));
        }

        /// <summary>POST /projects/invitations/{id}/accept</summary>
        [HttpPost("invitations/{invitationId:guid}/accept")]
        public async Task<IActionResult> ChapNhan(Guid invitationId, CancellationToken ct)
        {
            return Ok(await _loiMoi.ChapNhanAsync(invitationId, NguoiDung(), ct));
        }

        /// <summary>POST /projects/invitations/{id}/decline</summary>
        [HttpPost("invitations/{invitationId:guid}/decline")]
        public async Task<IActionResult> TuChoi(Guid invitationId, CancellationToken ct)
        {
            return Ok(await _loiMoi.TuChoiAsync(invitationId, NguoiDung(), ct));
        }

        /// <summary>GET /projects/{id}/invitations — chu du an.</summary>
        [HttpGet("{projectId:guid}/invitations")]
        public async Task<IActionResult> LoiMoiCuaDuAn(Guid projectId, CancellationToken ct)
        {
            return Ok(await _loiMoi.CuaDuAnAsync(projectId, Caller.TuHttp(HttpContext, ActorRole.Business), ct));
        }

        /// <summary>DELETE /projects/{id}/invitations/{invitationId} — thu hoi.</summary>
        [HttpDelete("{projectId:guid}/invitations/{invitationId:guid}")]
        public async Task<IActionResult> ThuHoi(Guid projectId, Guid invitationId, CancellationToken ct)
        {
            return Ok(await _loiMoi.ThuHoiAsync(projectId, invitationId, Caller.TuHttp(HttpContext, ActorRole.Business), ct));
        }

        private Caller NguoiDung()
        {
            return Caller.TuHttp(HttpContext, User.IsInRole(CrowdRoles.Business) ? ActorRole.Business : ActorRole.Labeler);
        }
    }

    /// <summary>Danh sach uu tien / chan va danh gia labeler cua doanh nghiep (FB-23).</summary>
    [ApiController]
    [Route("projects")]
    [Authorize]
    public sealed class QuanHeLabelerController : ControllerBase
    {
        private readonly QuanHeLabelerService _service;

        public QuanHeLabelerController(QuanHeLabelerService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            _service = service;
        }

        /// <summary>GET /projects/labeler-lists?kind=preferred|blocked</summary>
        [HttpGet("labeler-lists")]
        [Authorize(Roles = CrowdRoles.Business)]
        public async Task<IActionResult> DanhSach([FromQuery] OwnerListKind? kind, CancellationToken ct)
        {
            return Ok(await _service.DanhSachAsync(kind, DoanhNghiep(), ct));
        }

        /// <summary>PUT /projects/labeler-lists/{labelerId} { kind, note }</summary>
        [HttpPut("labeler-lists/{labelerId:guid}")]
        [Authorize(Roles = CrowdRoles.Business)]
        public async Task<IActionResult> Dat(Guid labelerId, [FromBody] OwnerListRequest body, CancellationToken ct)
        {
            return Ok(await _service.DatAsync(labelerId, body, DoanhNghiep(), ct));
        }

        /// <summary>PUT /projects/labeler-lists/by-username/{username} { kind, note }</summary>
        [HttpPut("labeler-lists/by-username/{username}")]
        [Authorize(Roles = CrowdRoles.Business)]
        public async Task<IActionResult> DatTheoUsername(string username, [FromBody] OwnerListRequest body, CancellationToken ct)
        {
            return Ok(await _service.DatTheoUsernameAsync(username, body, DoanhNghiep(), ct));
        }

        /// <summary>DELETE /projects/labeler-lists/{labelerId}</summary>
        [HttpDelete("labeler-lists/{labelerId:guid}")]
        [Authorize(Roles = CrowdRoles.Business)]
        public async Task<IActionResult> Xoa(Guid labelerId, CancellationToken ct)
        {
            await _service.XoaAsync(labelerId, DoanhNghiep(), ct);
            return NoContent();
        }

        /// <summary>POST /projects/{id}/members/invite-preferred — moi ca danh sach uu tien.</summary>
        [HttpPost("{projectId:guid}/members/invite-preferred")]
        public async Task<IActionResult> MoiUuTien(Guid projectId, CancellationToken ct)
        {
            return Ok(await _service.MoiUuTienAsync(projectId, DoanhNghiep(), ct));
        }

        /// <summary>PUT /projects/{id}/ratings/{labelerId} { stars, comment }</summary>
        [HttpPut("{projectId:guid}/ratings/{labelerId:guid}")]
        public async Task<IActionResult> DanhGia(Guid projectId, Guid labelerId, [FromBody] RatingRequest body, CancellationToken ct)
        {
            return Ok(await _service.DanhGiaAsync(projectId, labelerId, body, DoanhNghiep(), ct));
        }

        /// <summary>GET /projects/{id}/ratings</summary>
        [HttpGet("{projectId:guid}/ratings")]
        public async Task<IActionResult> DanhGiaCuaDuAn(Guid projectId, CancellationToken ct)
        {
            return Ok(await _service.DanhGiaCuaDuAnAsync(projectId, DoanhNghiep(), ct));
        }

        /// <summary>GET /projects/labelers/{labelerId}/rating-summary</summary>
        [HttpGet("labelers/{labelerId:guid}/rating-summary")]
        public async Task<IActionResult> TongHop(Guid labelerId, CancellationToken ct)
        {
            ActorRole vaiTro = User.IsInRole(CrowdRoles.Admin) ? ActorRole.Admin : ActorRole.Labeler;
            return Ok(await _service.TongHopAsync(labelerId, User.IsInRole(CrowdRoles.Business), Caller.TuHttp(HttpContext, vaiTro), ct));
        }

        private Caller DoanhNghiep()
        {
            return Caller.TuHttp(HttpContext, ActorRole.Business);
        }
    }
}
