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

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Xem(Guid id, CancellationToken ct)
        {
            return Ok(await _service.XemAsync(id, Caller.TuHttp(HttpContext, ActorRole.Business), ct));
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
