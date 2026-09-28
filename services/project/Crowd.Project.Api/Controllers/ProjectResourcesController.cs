using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Messaging;
using Crowd.Project.Api.Dtos;
using Crowd.Project.Api.Helpers;
using Crowd.Project.Api.Services;
using Crowd.Project.Domain.Common;
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

        /// <summary>POST /projects/{id}/members — chu du an moi nguoi (FP-05).</summary>
        [HttpPost("members")]
        public async Task<IActionResult> Them(Guid projectId, [FromBody] AddMemberRequest body, CancellationToken ct)
        {
            return Ok(await _service.ThemAsync(projectId, body, ChuDuAn(), ct));
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

    /// <summary>Dataset va mau (FB-11).</summary>
    [ApiController]
    [Route("projects/{projectId:guid}")]
    [Authorize]
    public sealed class DatasetsController : ControllerBase
    {
        /// <summary>Tran kich thuoc file ZIP: 200 MB.</summary>
        public const long KichThuocToiDa = 200L * 1024 * 1024;

        private readonly DatasetService _service;

        public DatasetsController(DatasetService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            _service = service;
        }

        /// <summary>
        /// POST /projects/{id}/datasets — multipart/form-data: name + file (ZIP anh).
        ///
        /// RequestSizeLimit NANG tran cho RIENG endpoint nay; moi endpoint khac van
        /// giu tran mac dinh 30 MB cua Kestrel.
        /// </summary>
        [HttpPost("datasets")]
        [RequestSizeLimit(KichThuocToiDa)]
        [RequestFormLimits(MultipartBodyLengthLimit = KichThuocToiDa)]
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

        /// <summary>GET /projects/{id}/samples?page=&amp;pageSize= — kem link xem anh 5 phut.</summary>
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
