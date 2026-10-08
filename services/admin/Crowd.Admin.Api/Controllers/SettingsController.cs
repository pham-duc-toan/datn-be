using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Admin.Api.Dtos;
using Crowd.Admin.Api.Services;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crowd.Admin.Api.Controllers
{
    /// <summary>Setting he thong — CHI admin. Moi lan doi ghi lich su va lan toi moi service qua event.</summary>
    [ApiController]
    [Route("admin/settings")]
    [Authorize(Roles = CrowdRoles.Admin)]
    public sealed class SettingsController : ControllerBase
    {
        private readonly SettingService _service;

        public SettingsController(SettingService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            _service = service;
        }

        /// <summary>GET /admin/settings?group=... — moi setting kem dinh nghia (kieu, gioi han, mo ta) va gia tri hien tai.</summary>
        [HttpGet]
        public async Task<IActionResult> DanhSach([FromQuery] string? group, CancellationToken ct)
        {
            return Ok(await _service.DanhSachAsync(group, ct));
        }

        [HttpGet("{key}")]
        public async Task<IActionResult> ChiTiet(string key, CancellationToken ct)
        {
            return Ok(await _service.ChiTietAsync(key, ct));
        }

        /// <summary>GET /admin/settings/{key}/history — ai doi, luc nao, cu → moi, ly do.</summary>
        [HttpGet("{key}/history")]
        public async Task<IActionResult> LichSu(string key, CancellationToken ct)
        {
            return Ok(await _service.LichSuAsync(key, ct));
        }

        /// <summary>PUT /admin/settings/{key}  { "value": 25, "reason": "..." }</summary>
        [HttpPut("{key}")]
        public async Task<IActionResult> Doi(string key, [FromBody] UpdateSettingRequest body, CancellationToken ct)
        {
            Guid admin = Caller.TuHttp(HttpContext, ActorRole.Admin).LayUserId();
            return Ok(await _service.DoiAsync(key, body, admin, ct));
        }
    }
}
