using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Messaging;
using Crowd.Project.Api.Dtos;
using Crowd.Project.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crowd.Project.Api.Controllers
{
    /// <summary>
    /// Chu du an xem du an qua mat labeler (kien truc 3.39): mau xem truoc tren cho du an (P1) va bai xem thu bang du lieu
    /// that (Q2). Chi chu du an (BOLA trong ProjectAccessService).
    /// </summary>
    [ApiController]
    [Route("projects")]
    [Authorize(Roles = CrowdRoles.Business + "," + CrowdRoles.Admin)]
    public sealed class XemTruocController : ControllerBase
    {
        private readonly XemTruocService _service;

        public XemTruocController(XemTruocService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            _service = service;
        }

        /// <summary>PUT /projects/{id}/preview { enabled } — bat / tat mau xem truoc tren the cho du an (mac dinh bat).</summary>
        [HttpPut("{id:guid}/preview")]
        public async Task<IActionResult> DatXemTruoc(Guid id, [FromBody] SetPreviewRequest body, CancellationToken ct)
        {
            return Ok(await _service.DatXemTruocAsync(id, body, ChuDuAn(), ct));
        }

        /// <summary>GET /projects/{id}/trial — bai xem thu nhu labeler (khong ghi gi).</summary>
        [HttpGet("{id:guid}/trial")]
        public async Task<IActionResult> BaiXemThu(Guid id, CancellationToken ct)
        {
            return Ok(await _service.TaoBaiXemThuAsync(id, ChuDuAn(), ct));
        }

        /// <summary>POST /projects/{id}/trial/check { answers } — cham ngay cau vang cua bai xem thu.</summary>
        [HttpPost("{id:guid}/trial/check")]
        public async Task<IActionResult> ChamXemThu(Guid id, [FromBody] SubmitAttemptRequest? body, CancellationToken ct)
        {
            return Ok(await _service.ChamBaiXemThuAsync(id, body, ChuDuAn(), ct));
        }

        private Caller ChuDuAn()
        {
            return Caller.TuHttp(HttpContext, ActorRole.Business);
        }
    }
}
