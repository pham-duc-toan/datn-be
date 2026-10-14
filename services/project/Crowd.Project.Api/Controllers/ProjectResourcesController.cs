using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Messaging;
using Crowd.Project.Api.Dtos;
using Crowd.Project.Api.Helpers;
using Crowd.Project.Api.Services;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.Members;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Crowd.Project.Api.Controllers
{
    /// <summary>Thanh vien du an + labeler tu tham gia.</summary>
    [ApiController]
    [Route("projects/{projectId:guid}")]
    [Authorize]
    public sealed class MembersController : ControllerBase
    {
        private readonly MemberService _service;

        public MembersController(MemberService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            _service = service;
        }

        [HttpGet("members")]
        public async Task<IActionResult> DanhSach(Guid projectId, CancellationToken ct)
        {
            return Ok(await _service.DanhSachAsync(projectId, ChuDuAn(), ct));
        }

        /// <summary>POST /projects/{id}/members — chu du an moi MOT nguoi theo userId hoac email (FP-05).</summary>
        [HttpPost("members")]
        public async Task<IActionResult> Them(Guid projectId, [FromBody] AddMemberRequest body, CancellationToken ct)
        {
            return Ok(await _service.ThemAsync(projectId, body, ChuDuAn(), ct));
        }

        /// <summary>POST /projects/{id}/members/batch — moi nhieu nguoi (userIds / emails), ket qua tung nguoi.</summary>
        [HttpPost("members/batch")]
        public async Task<IActionResult> ThemNhieu(Guid projectId, [FromBody] AddMembersBatchRequest body, CancellationToken ct)
        {
            return Ok(await _service.ThemNhieuAsync(projectId, body, ChuDuAn(), ct));
        }

        /// <summary>POST /projects/{id}/members/{userId}/block (FB-23)</summary>
        [HttpPost("members/{userId:guid}/block")]
        public async Task<IActionResult> Chan(Guid projectId, Guid userId, CancellationToken ct)
        {
            return Ok(await _service.ChanAsync(projectId, userId, ChuDuAn(), ct));
        }

        [HttpPost("members/{userId:guid}/unblock")]
        public async Task<IActionResult> BoChan(Guid projectId, Guid userId, CancellationToken ct)
        {
            return Ok(await _service.BoChanAsync(projectId, userId, ChuDuAn(), ct));
        }

        [HttpDelete("members/{userId:guid}")]
        public async Task<IActionResult> Xoa(Guid projectId, Guid userId, CancellationToken ct)
        {
            await _service.XoaAsync(projectId, userId, ChuDuAn(), ct);
            return NoContent();
        }

        /// <summary>POST /projects/{id}/join — labeler tu tham gia du an khong can test.</summary>
        [HttpPost("join")]
        [Authorize(Roles = CrowdRoles.Labeler)]
        public async Task<IActionResult> ThamGia(Guid projectId, CancellationToken ct)
        {
            return Ok(await _service.ThamGiaAsync(projectId, Caller.TuHttp(HttpContext, ActorRole.Labeler), ct));
        }

        private Caller ChuDuAn()
        {
            return Caller.TuHttp(HttpContext, ActorRole.Business);
        }
    }

    /// <summary>Ma moi tham gia du an: chu du an tao / xem / thu hoi; labeler nhap ma de vao.</summary>
    [ApiController]
    [Route("projects")]
    [Authorize]
    public sealed class InviteCodesController : ControllerBase
    {
        private readonly InviteCodeService _service;

        public InviteCodesController(InviteCodeService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            _service = service;
        }

        /// <summary>POST /projects/{id}/invite-codes — { maxUses?, expiresInHours?, note? }.</summary>
        [HttpPost("{projectId:guid}/invite-codes")]
        public async Task<IActionResult> Tao(Guid projectId, [FromBody] CreateInviteCodeRequest body, CancellationToken ct)
        {
            InviteCodeResponse ma = await _service.TaoAsync(projectId, body, ChuDuAn(), ct);
            return Created("/projects/" + projectId + "/invite-codes", ma);
        }

        [HttpGet("{projectId:guid}/invite-codes")]
        public async Task<IActionResult> DanhSach(Guid projectId, CancellationToken ct)
        {
            return Ok(await _service.DanhSachAsync(projectId, ChuDuAn(), ct));
        }

        /// <summary>DELETE /projects/{id}/invite-codes/{codeId} — thu hoi (giu lai dong de xem lich su).</summary>
        [HttpDelete("{projectId:guid}/invite-codes/{codeId:guid}")]
        public async Task<IActionResult> ThuHoi(Guid projectId, Guid codeId, CancellationToken ct)
        {
            return Ok(await _service.ThuHoiAsync(projectId, codeId, ChuDuAn(), ct));
        }

        /// <summary>POST /projects/join-by-code — { code }: labeler vao du an cua ma (ke ca rieng tu).</summary>
        [HttpPost("join-by-code")]
        [Authorize(Roles = CrowdRoles.Labeler)]
        public async Task<IActionResult> VaoBangMa([FromBody] JoinByCodeRequest body, CancellationToken ct)
        {
            return Ok(await _service.VaoBangMaAsync(body, Caller.TuHttp(HttpContext, ActorRole.Labeler), ct));
        }

        private Caller ChuDuAn()
        {
            return Caller.TuHttp(HttpContext, ActorRole.Business);
        }
    }

    /// <summary>Don xin tham gia du an che do approval.</summary>
    [ApiController]
    [Route("projects")]
    [Authorize]
    public sealed class JoinRequestsController : ControllerBase
    {
        private readonly JoinRequestService _service;

        public JoinRequestsController(JoinRequestService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            _service = service;
        }

        /// <summary>POST /projects/{id}/join-requests — labeler gui don { message? }.</summary>
        [HttpPost("{projectId:guid}/join-requests")]
        [Authorize(Roles = CrowdRoles.Labeler)]
        public async Task<IActionResult> Gui(Guid projectId, [FromBody] CreateJoinRequestRequest body, CancellationToken ct)
        {
            JoinRequestResponse don = await _service.GuiAsync(projectId, body, Labeler(), ct);
            return Created("/projects/join-requests/mine", don);
        }

        /// <summary>DELETE /projects/{id}/join-requests/mine — labeler rut don dang cho.</summary>
        [HttpDelete("{projectId:guid}/join-requests/mine")]
        [Authorize(Roles = CrowdRoles.Labeler)]
        public async Task<IActionResult> Rut(Guid projectId, CancellationToken ct)
        {
            return Ok(await _service.RutAsync(projectId, Labeler(), ct));
        }

        /// <summary>GET /projects/join-requests/mine — moi don cua toi.</summary>
        [HttpGet("join-requests/mine")]
        [Authorize(Roles = CrowdRoles.Labeler)]
        public async Task<IActionResult> CuaToi(CancellationToken ct)
        {
            return Ok(await _service.CuaToiAsync(Labeler(), ct));
        }

        /// <summary>GET /projects/{id}/join-requests?status=pending — hang doi cua chu du an.</summary>
        [HttpGet("{projectId:guid}/join-requests")]
        public async Task<IActionResult> DanhSach(Guid projectId, [FromQuery] JoinRequestStatus? status, CancellationToken ct)
        {
            return Ok(await _service.DanhSachAsync(projectId, status, ChuDuAn(), ct));
        }

        [HttpPost("{projectId:guid}/join-requests/{requestId:guid}/approve")]
        public async Task<IActionResult> Duyet(Guid projectId, Guid requestId, CancellationToken ct)
        {
            return Ok(await _service.DuyetAsync(projectId, requestId, ChuDuAn(), ct));
        }

        /// <summary>POST .../reject — { reason? }.</summary>
        [HttpPost("{projectId:guid}/join-requests/{requestId:guid}/reject")]
        public async Task<IActionResult> TuChoi(Guid projectId, Guid requestId, [FromBody] RejectJoinRequestRequest body, CancellationToken ct)
        {
            return Ok(await _service.TuChoiAsync(projectId, requestId, body, ChuDuAn(), ct));
        }

        private Caller Labeler()
        {
            return Caller.TuHttp(HttpContext, ActorRole.Labeler);
        }

        private Caller ChuDuAn()
        {
            return Caller.TuHttp(HttpContext, ActorRole.Business);
        }
    }

    /// <summary>Dataset va mau (FB-11).</summary>
    [ApiController]
    [Route("projects/{projectId:guid}")]
    [Authorize]
    public sealed class DatasetsController : ControllerBase
    {
        private readonly DatasetService _service;
        private readonly UploadService _uploads;

        public DatasetsController(DatasetService service, UploadService uploads)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            if (uploads == null)
            {
                throw new ArgumentNullException(nameof(uploads));
            }

            _service = service;
            _uploads = uploads;
        }

        /// <summary>
        /// POST /projects/{id}/uploads — xin link ky san de PUT file THANG len MinIO
        /// (audio / video / anh lon / file manifest .jsonl). Moi file mot { key, uploadUrl }.
        /// </summary>
        [HttpPost("uploads")]
        public async Task<IActionResult> XinLinkUpload(Guid projectId, [FromBody] CreateUploadsRequest body, CancellationToken ct)
        {
            return Ok(await _uploads.TaoLinkAsync(projectId, body, Caller.TuHttp(HttpContext, ActorRole.Business), ct));
        }

        /// <summary>
        /// POST /projects/{id}/datasets/manifest — tao lo tu manifest (moi loai du lieu):
        /// {"name":..., "rows":[...]} hoac {"name":..., "manifestKey":"&lt;key file .jsonl&gt;"}.
        /// Tra 202: lo o trang thai Pending, worker xu ly nen — xem GET /datasets.
        /// </summary>
        [HttpPost("datasets/manifest")]
        public async Task<IActionResult> NapManifest(Guid projectId, [FromBody] CreateManifestDatasetRequest body, CancellationToken ct)
        {
            DatasetResponse d = await _service.TaoTuManifestAsync(
                projectId, body, Caller.TuHttp(HttpContext, ActorRole.Business), ct);

            return Accepted("/projects/" + projectId + "/datasets", d);
        }

        /// <summary>
        /// POST /projects/{id}/datasets — multipart/form-data: name + file (ZIP anh).
        ///
        /// GioiHanZipFilter NANG tran cho RIENG endpoint nay theo setting
        /// dataset.zip_max_bytes; moi endpoint khac van giu tran mac dinh 30 MB cua Kestrel.
        /// </summary>
        [HttpPost("datasets")]
        [ServiceFilter(typeof(GioiHanZipFilter))]
        public async Task<IActionResult> Nap(Guid projectId, [FromForm] string? name, IFormFile? file, CancellationToken ct)
        {
            if (file == null || file.Length == 0)
            {
                throw new InvalidValueException("thieu_file", "Can file ZIP (truong 'file').");
            }

            using (System.IO.Stream luong = file.OpenReadStream())
            {
                DatasetResponse d = await _service.NapZipAsync(
                    projectId,
                    name ?? file.FileName,
                    luong,
                    Caller.TuHttp(HttpContext, ActorRole.Business),
                    ct);

                return Created("/projects/" + projectId + "/datasets", d);
            }
        }

        [HttpGet("datasets")]
        public async Task<IActionResult> DanhSach(Guid projectId, CancellationToken ct)
        {
            return Ok(await _service.DanhSachAsync(projectId, Caller.TuHttp(HttpContext, ActorRole.Business), ct));
        }

        /// <summary>GET /projects/{id}/samples?page=&amp;pageSize= — kem link xem file 5 phut (hoac noi dung text).</summary>
        [HttpGet("samples")]
        public async Task<IActionResult> Mau(Guid projectId, [FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct)
        {
            return Ok(await _service.DanhSachMauAsync(
                projectId, page, pageSize, Caller.TuHttp(HttpContext, ActorRole.Business), ct));
        }
    }

    /// <summary>Cau hoi vang (FB-16).</summary>
    [ApiController]
    [Route("projects/{projectId:guid}/gold-items")]
    [Authorize]
    public sealed class GoldItemsController : ControllerBase
    {
        private readonly GoldSetService _service;

        public GoldItemsController(GoldSetService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> DanhSach(Guid projectId, CancellationToken ct)
        {
            return Ok(await _service.DanhSachAsync(projectId, ChuDuAn(), ct));
        }

        [HttpPost]
        public async Task<IActionResult> Them(Guid projectId, [FromBody] AddGoldItemsRequest body, CancellationToken ct)
        {
            return Ok(await _service.ThemAsync(projectId, body, ChuDuAn(), ct));
        }

        [HttpDelete("{goldItemId:guid}")]
        public async Task<IActionResult> Xoa(Guid projectId, Guid goldItemId, CancellationToken ct)
        {
            await _service.XoaAsync(projectId, goldItemId, ChuDuAn(), ct);
            return NoContent();
        }

        private Caller ChuDuAn()
        {
            return Caller.TuHttp(HttpContext, ActorRole.Business);
        }
    }

    /// <summary>Bai test dau vao (FL-03). Chi labeler.</summary>
    [ApiController]
    [Route("projects/{projectId:guid}/entrance-test")]
    [Authorize(Roles = CrowdRoles.Labeler)]
    public sealed class EntranceTestController : ControllerBase
    {
        private readonly EntranceTestService _service;

        public EntranceTestController(EntranceTestService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            _service = service;
        }

        /// <summary>POST /projects/{id}/entrance-test/attempts — bat dau, nhan cau hoi.</summary>
        [HttpPost("attempts")]
        public async Task<IActionResult> BatDau(Guid projectId, CancellationToken ct)
        {
            return Ok(await _service.BatDauAsync(projectId, Labeler(), ct));
        }

        /// <summary>POST /projects/{id}/entrance-test/attempts/{attemptId}/submit</summary>
        [HttpPost("attempts/{attemptId:guid}/submit")]
        public async Task<IActionResult> Nop(
            Guid projectId, Guid attemptId, [FromBody] SubmitEntranceTestRequest body, CancellationToken ct)
        {
            return Ok(await _service.NopAsync(projectId, attemptId, body, Labeler(), ct));
        }

        [HttpGet("attempts")]
        public async Task<IActionResult> LichSu(Guid projectId, CancellationToken ct)
        {
            return Ok(await _service.LichSuAsync(projectId, Labeler(), ct));
        }

        private Caller Labeler()
        {
            return Caller.TuHttp(HttpContext, ActorRole.Labeler);
        }
    }
}
