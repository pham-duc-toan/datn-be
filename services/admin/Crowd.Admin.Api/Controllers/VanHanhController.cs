using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Admin.Api.Dtos;
using Crowd.Admin.Api.Services;
using Crowd.Admin.Api.Workers;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crowd.Admin.Api.Controllers
{
    /// <summary>Hang doi duyet chung, quan ly mau bai toan (FM-03), do sau DLQ. Chi admin.</summary>
    [ApiController]
    [Route("admin")]
    [Authorize(Roles = CrowdRoles.Admin)]
    public sealed class VanHanhController : ControllerBase
    {
        private readonly HangDoiDuyet _hangDoi;
        private readonly MauBaiToanService _mau;
        private readonly TrangThaiDlq _dlq;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;

        public VanHanhController(HangDoiDuyet hangDoi, MauBaiToanService mau, TrangThaiDlq dlq, ISettings settings, TimeProvider clock)
        {
            if (hangDoi == null)
            {
                throw new ArgumentNullException(nameof(hangDoi));
            }

            if (mau == null)
            {
                throw new ArgumentNullException(nameof(mau));
            }

            if (dlq == null)
            {
                throw new ArgumentNullException(nameof(dlq));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _hangDoi = hangDoi;
            _mau = mau;
            _dlq = dlq;
            _settings = settings;
            _clock = clock;
        }

        // ---- Hang doi duyet chung ----

        /// <summary>GET /admin/approvals?kind=&amp;status=pending|waiting|approved|rejected|cancelled|resolved|all&amp;page=&amp;pageSize=</summary>
        [HttpGet("approvals")]
        public async Task<IActionResult> HangDoi([FromQuery] string? kind, [FromQuery] string? status,
            [FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct)
        {
            return Ok(await _hangDoi.DanhSachAsync(kind, status, page, pageSize, ct));
        }

        /// <summary>GET /admin/approvals/summary — so don cho duyet theo loai + don cu nhat cho bao lau.</summary>
        [HttpGet("approvals/summary")]
        public async Task<IActionResult> TomTat(CancellationToken ct)
        {
            return Ok(await _hangDoi.TomTatAsync(_clock.GetUtcNow(), ct));
        }

        /// <summary>POST /admin/approvals/rebuild — dung lai hang doi tu nhat ky audit.</summary>
        [HttpPost("approvals/rebuild")]
        public async Task<IActionResult> DungLai(CancellationToken ct)
        {
            int so = await _hangDoi.DungLaiAsync(ct);
            return Ok(new { appliedEvents = so });
        }

        // ---- Mau bai toan ----

        /// <summary>GET /admin/templates?modality= — ca mau da tat.</summary>
        [HttpGet("templates")]
        public async Task<IActionResult> DanhSachMau([FromQuery] string? modality, CancellationToken ct)
        {
            return Ok(await _mau.DanhSachAsync(modality, true, ct));
        }

        [HttpGet("templates/{id:guid}")]
        public async Task<IActionResult> LayMau(Guid id, CancellationToken ct)
        {
            return Ok(await _mau.LayAsync(id, true, ct));
        }

        /// <summary>POST /admin/templates { code, name, description, labelSchema, guidelineMarkdown, suggestedUnitPriceVnd, suggestedRedundancy }</summary>
        [HttpPost("templates")]
        public async Task<IActionResult> TaoMau([FromBody] TemplateRequest body, CancellationToken ct)
        {
            TemplateResponse m = await _mau.TaoAsync(body, AdminId(), ct);
            return Created("/templates/" + m.Id, m);
        }

        /// <summary>PUT /admin/templates/{id} — sua (code khong doi).</summary>
        [HttpPut("templates/{id:guid}")]
        public async Task<IActionResult> SuaMau(Guid id, [FromBody] TemplateRequest body, CancellationToken ct)
        {
            return Ok(await _mau.SuaAsync(id, body, AdminId(), ct));
        }

        [HttpPost("templates/{id:guid}/deactivate")]
        public async Task<IActionResult> TatMau(Guid id, CancellationToken ct)
        {
            return Ok(await _mau.DatHoatDongAsync(id, false, AdminId(), ct));
        }

        [HttpPost("templates/{id:guid}/activate")]
        public async Task<IActionResult> BatMau(Guid id, CancellationToken ct)
        {
            return Ok(await _mau.DatHoatDongAsync(id, true, AdminId(), ct));
        }

        private Guid AdminId()
        {
            return Caller.TuHttp(HttpContext, ActorRole.Admin).LayUserId();
        }

        // ---- Van hanh ----

        /// <summary>GET /admin/ops/queues — do sau cac DLQ o lan kiem tra gan nhat (DlqMonitorWorker).</summary>
        [HttpGet("ops/queues")]
        public IActionResult HangDoiDlq()
        {
            var doc = _dlq.Doc();
            int nguong = _settings.SoNguyen(SettingKeys.OpsDlqAlertThreshold);
            List<QueueDepthResponse> ds = doc.HangDoi.Select(q => new QueueDepthResponse
            {
                Queue = q.Key,
                Messages = q.Value,
                IsDeadLetter = true,
                OverThreshold = q.Value >= nguong,
            }).ToList();
            return Ok(new OpsQueuesResponse { Reachable = doc.DocDuoc, CheckedAt = doc.Luc, Threshold = nguong, DeadLetterQueues = ds });
        }
    }

    /// <summary>Danh muc mau bai toan cho MOI nguoi dang nhap (doanh nghiep chon khi tao du an) — chi mau dang bat.</summary>
    [ApiController]
    [Route("templates")]
    [Authorize]
    public sealed class TemplatesController : ControllerBase
    {
        private readonly MauBaiToanService _mau;

        public TemplatesController(MauBaiToanService mau)
        {
            if (mau == null)
            {
                throw new ArgumentNullException(nameof(mau));
            }

            _mau = mau;
        }

        /// <summary>GET /templates?modality=image</summary>
        [HttpGet("")]
        public async Task<IActionResult> DanhSach([FromQuery] string? modality, CancellationToken ct)
        {
            return Ok(await _mau.DanhSachAsync(modality, false, ct));
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Lay(Guid id, CancellationToken ct)
        {
            return Ok(await _mau.LayAsync(id, false, ct));
        }
    }
}
