using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Correlation;
using Crowd.BuildingBlocks.Messaging;
using Crowd.Payment.Api.Dtos;
using Crowd.Payment.Api.Services;
using Crowd.Payment.Domain.Deposits;
using Crowd.Payment.Infrastructure.Providers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace Crowd.Payment.Api.Controllers
{
    /// <summary>Nap tien (FB-03) — doanh nghiep.</summary>
    [ApiController]
    [Route("payments/deposits")]
    [Authorize(Roles = CrowdRoles.Business)]
    public sealed class DepositsController : ControllerBase
    {
        private readonly DepositService _service;

        public DepositsController(DepositService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            _service = service;
        }

        /// <summary>POST /payments/deposits — header Idempotency-Key bat buoc.</summary>
        [HttpPost]
        public async Task<IActionResult> Tao(
            [FromBody] CreateDepositRequest body,
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
            CancellationToken ct)
        {
            DepositResponse d = await _service.TaoAsync(body == null ? 0 : body.AmountVnd, idempotencyKey, Caller.TuHttp(HttpContext, ActorRole.Business), ct);
            return Ok(d);
        }

        /// <summary>
        /// POST /payments/deposits/manual — nap bang chuyen khoan ngan hang thu cong.
        /// Tra ve bankInfo + transferCode (noi dung chuyen khoan). Header Idempotency-Key bat buoc.
        /// </summary>
        [HttpPost("manual")]
        public async Task<IActionResult> TaoChuyenKhoan(
            [FromBody] CreateDepositRequest body,
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
            CancellationToken ct)
        {
            DepositResponse d = await _service.TaoChuyenKhoanAsync(
                body == null ? 0 : body.AmountVnd, idempotencyKey, Caller.TuHttp(HttpContext, ActorRole.Business), ct);
            return Ok(d);
        }

        /// <summary>POST /payments/deposits/{id}/transferred — "toi da chuyen khoan", cho admin doi chieu.</summary>
        [HttpPost("{id:guid}/transferred")]
        public async Task<IActionResult> DaChuyen(Guid id, CancellationToken ct)
        {
            return Ok(await _service.BaoDaChuyenAsync(id, Caller.TuHttp(HttpContext, ActorRole.Business), ct));
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Xem(Guid id, CancellationToken ct)
        {
            return Ok(await _service.XemAsync(id, Caller.TuHttp(HttpContext, ActorRole.Business), ct));
        }
    }

    /// <summary>Admin doi chieu sao ke va duyet / tu choi lenh nap chuyen khoan thu cong.</summary>
    [ApiController]
    [Route("payments/admin/deposits")]
    [Authorize(Roles = CrowdRoles.Admin)]
    public sealed class AdminDepositsController : ControllerBase
    {
        private readonly DepositService _service;

        public AdminDepositsController(DepositService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            _service = service;
        }

        /// <summary>GET /payments/admin/deposits?status=awaitingApproval&amp;page=&amp;pageSize=</summary>
        [HttpGet]
        public async Task<IActionResult> DanhSach(
            [FromQuery] PaymentIntentStatus? status, [FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct)
        {
            return Ok(await _service.DanhSachChuyenKhoanAsync(status, page, pageSize, ct));
        }

        /// <summary>POST /payments/admin/deposits/{id}/approve {"bankTxnRef": "..."} — tien vao vi doanh nghiep.</summary>
        [HttpPost("{id:guid}/approve")]
        public async Task<IActionResult> Duyet(Guid id, [FromBody] ApproveDepositRequest? body, CancellationToken ct)
        {
            string? ma = body == null ? null : body.BankTxnRef;
            return Ok(await _service.DuyetChuyenKhoanAsync(id, ma, Caller.TuHttp(HttpContext, ActorRole.Admin), ct));
        }

        /// <summary>POST /payments/admin/deposits/{id}/reject {"reason": "..."}</summary>
        [HttpPost("{id:guid}/reject")]
        public async Task<IActionResult> TuChoi(Guid id, [FromBody] RejectDepositRequest? body, CancellationToken ct)
        {
            string? lyDo = body == null ? null : body.Reason;
            return Ok(await _service.TuChoiChuyenKhoanAsync(id, lyDo, Caller.TuHttp(HttpContext, ActorRole.Admin), ct));
        }
    }

    /// <summary>
    /// Webhook tu cong thanh toan. [AllowAnonymous] vi cong khong co JWT cua ta —
    /// thay vao do la CHU KY HMAC tren body, kiem trong DepositService.
    /// </summary>
    [ApiController]
    [Route("payments/webhooks")]
    [AllowAnonymous]
    public sealed class WebhooksController : ControllerBase
    {
        private readonly DepositService _service;

        public WebhooksController(DepositService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            _service = service;
        }

        /// <summary>POST /payments/webhooks/sandbox — header X-Sandbox-Signature = HMAC-SHA256(body).</summary>
        [HttpPost("sandbox")]
        public async Task<IActionResult> Sandbox(CancellationToken ct)
        {
            // Doc CHUOI GOC cua body: chu ky tinh tren dung tung byte gui len.
            string body;
            using (StreamReader r = new StreamReader(Request.Body, Encoding.UTF8))
            {
                body = await r.ReadToEndAsync(ct);
            }

            string? chuKy = Request.Headers["X-Sandbox-Signature"];
            await _service.XuLyWebhookAsync(body, chuKy, LayCorrelation(), ct);
            return Ok(new { received = true });
        }

        private Guid LayCorrelation()
        {
            Guid g;
            if (Guid.TryParse(Request.Headers[CorrelationHeaders.HeaderName], out g))
            {
                return g;
            }

            return Guid.CreateVersion7();
        }
    }

    /// <summary>
    /// CHI MOI TRUONG DEV: gia lap trang thanh toan cua cong. Bam "tra tien" thi
    /// cong gia lap ky va goi webhook — di qua DUNG duong kiem chu ky nhu cong that.
    /// </summary>
    [ApiController]
    [Route("payments/sandbox")]
    [AllowAnonymous]
    public sealed class SandboxCheckoutController : ControllerBase
    {
        private static readonly JsonSerializerOptions JsonWebhook = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        private readonly DepositService _service;
        private readonly IPaymentProvider _provider;
        private readonly IWebHostEnvironment _env;

        public SandboxCheckoutController(DepositService service, IPaymentProvider provider, IWebHostEnvironment env)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            if (env == null)
            {
                throw new ArgumentNullException(nameof(env));
            }

            _service = service;
            _provider = provider;
            _env = env;
        }

        /// <summary>POST /payments/sandbox/checkout/{intentId}/pay?amount=</summary>
        [HttpPost("checkout/{intentId:guid}/pay")]
        public async Task<IActionResult> TraTien(Guid intentId, [FromQuery] long amount, CancellationToken ct)
        {
            if (!_env.IsDevelopment())
            {
                return NotFound();
            }

            SandboxPaymentProvider? sandbox = _provider as SandboxPaymentProvider;
            if (sandbox == null)
            {
                return NotFound();
            }

            SandboxWebhookBody w = new SandboxWebhookBody
            {
                IntentId = intentId,
                AmountVnd = amount,
                Status = "success",
                ProviderTxnId = "SBX-D-" + intentId.ToString("N"),
            };

            string body = JsonSerializer.Serialize(w, JsonWebhook);
            await _service.XuLyWebhookAsync(body, sandbox.Ky(body), Guid.CreateVersion7(), ct);
            return Ok(new { paid = true, providerTxnId = w.ProviderTxnId });
        }
    }
}
