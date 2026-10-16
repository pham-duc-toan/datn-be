using System;
using System.Collections.Generic;
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
using Crowd.Payment.Api.Exceptions;
using Crowd.Payment.Api.Services;
using Crowd.Payment.Domain.Common;
using Crowd.Payment.Domain.Deposits;
using Crowd.Payment.Infrastructure.Providers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

        /// <summary>GET /payments/deposits/providers — cac cong nap dang mo de doanh nghiep chon.</summary>
        [HttpGet("providers")]
        public IActionResult CacCong()
        {
            return Ok(new PaymentProvidersResponse { Providers = _service.CacCongDangMo() });
        }

        /// <summary>POST /payments/deposits { amountVnd, provider: sandbox|vnpay|momo } — header Idempotency-Key bat buoc.</summary>
        [HttpPost]
        public async Task<IActionResult> Tao(
            [FromBody] CreateDepositRequest body,
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
            CancellationToken ct)
        {
            string? ip = HttpContext.Connection.RemoteIpAddress == null ? null : HttpContext.Connection.RemoteIpAddress.MapToIPv4().ToString();
            DepositResponse d = await _service.TaoAsync(
                body == null ? 0 : body.AmountVnd,
                body == null ? null : body.Provider,
                ip,
                idempotencyKey,
                Caller.TuHttp(HttpContext, ActorRole.Business),
                ct);
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

        /// <summary>
        /// POST /payments/deposits/{id}/sync — vua tra tien xong (trinh duyet ve tu VNPay / MoMo): hoi
        /// nguoc cong ngay. Tien CHI vao khi cong xac nhan; tham so tren URL tra ve khong duoc tin.
        /// </summary>
        [HttpPost("{id:guid}/sync")]
        public async Task<IActionResult> DongBo(Guid id, CancellationToken ct)
        {
            return Ok(await _service.DongBoAsync(id, Caller.TuHttp(HttpContext, ActorRole.Business), ct));
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
    /// Thong bao ket qua tu cong thanh toan. [AllowAnonymous] vi cong khong co JWT cua ta —
    /// thay vao do la CHU KY tren noi dung, adapter tung cong tu kiem.
    /// </summary>
    [ApiController]
    [Route("payments/webhooks")]
    [AllowAnonymous]
    public sealed class WebhooksController : ControllerBase
    {
        private readonly DepositService _service;
        private readonly IServiceProvider _sp;
        private readonly ILogger<WebhooksController> _logger;

        public WebhooksController(DepositService service, IServiceProvider sp, ILogger<WebhooksController> logger)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            if (sp == null)
            {
                throw new ArgumentNullException(nameof(sp));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _service = service;
            _sp = sp;
            _logger = logger;
        }

        /// <summary>POST /payments/webhooks/sandbox — header X-Sandbox-Signature = HMAC-SHA256(body).</summary>
        [HttpPost("sandbox")]
        public async Task<IActionResult> Sandbox(CancellationToken ct)
        {
            SandboxPaymentProvider? sandbox = _sp.GetService<SandboxPaymentProvider>();
            if (sandbox == null)
            {
                return NotFound();
            }

            // Doc CHUOI GOC cua body: chu ky tinh tren dung tung byte gui len.
            string body = await DocBodyAsync(ct);
            string? chuKy = Request.Headers["X-Sandbox-Signature"];
            await _service.XuLyThongBaoAsync(sandbox, sandbox.DocWebhook(body, chuKy), LayCorrelation(), ct);
            return Ok(new { received = true });
        }

        /// <summary>
        /// GET /payments/webhooks/vnpay?vnp_...&amp;vnp_SecureHash=... — IPN cua VNPay. Luon HTTP 200, ket qua
        /// nam trong RspCode theo quy uoc VNPay: 00 xong | 01 khong thay lenh | 02 da xac nhan | 04 sai so
        /// tien | 97 sai chu ky | 99 loi khac (VNPay se gui lai).
        /// </summary>
        [HttpGet("vnpay")]
        public async Task<IActionResult> VnPay(CancellationToken ct)
        {
            VnPayProvider? vnpay = _sp.GetService<VnPayProvider>();
            if (vnpay == null)
            {
                return NotFound();
            }

            Dictionary<string, string> q = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> kv in Request.Query)
            {
                q[kv.Key] = kv.Value.ToString();
            }

            try
            {
                bool moi = await _service.XuLyThongBaoAsync(vnpay, vnpay.DocIpn(q), LayCorrelation(), ct);
                if (!moi)
                {
                    return Ok(new VnPayIpnResponse { RspCode = "02", Message = "Order already confirmed" });
                }

                return Ok(new VnPayIpnResponse { RspCode = "00", Message = "Confirm Success" });
            }
            catch (ForbiddenException)
            {
                return Ok(new VnPayIpnResponse { RspCode = "97", Message = "Invalid signature" });
            }
            catch (NotFoundException)
            {
                return Ok(new VnPayIpnResponse { RspCode = "01", Message = "Order not found" });
            }
            catch (RuleViolationException ex) when (ex.Code == "sai_so_tien")
            {
                return Ok(new VnPayIpnResponse { RspCode = "04", Message = "Invalid amount" });
            }
            catch (RuleViolationException ex) when (ex.Code == "da_thanh_toan" || ex.Code == "lenh_nap_da_chot")
            {
                return Ok(new VnPayIpnResponse { RspCode = "02", Message = "Order already confirmed" });
            }
            catch (Exception ex) when (ex is RuleViolationException || ex is CongChuaXacNhanException || ex is InvalidValueException)
            {
                _logger.LogWarning(ex, "IPN VNPay chua xu ly duoc");
                return Ok(new VnPayIpnResponse { RspCode = "99", Message = "Unknown error" });
            }
        }

        /// <summary>
        /// POST /payments/webhooks/momo — IPN cua MoMo (JSON co chu ky). Xu ly xong → 204 (MoMo coi la da
        /// nhan); chu ky sai → 400; chua xac nhan duoc voi cong → 503 de MoMo gui lai.
        /// </summary>
        [HttpPost("momo")]
        public async Task<IActionResult> MoMo(CancellationToken ct)
        {
            MoMoProvider? momo = _sp.GetService<MoMoProvider>();
            if (momo == null)
            {
                return NotFound();
            }

            Dictionary<string, string>? p = DocJsonPhang(await DocBodyAsync(ct));
            if (p == null)
            {
                return BadRequest();
            }

            try
            {
                await _service.XuLyThongBaoAsync(momo, momo.DocIpn(p), LayCorrelation(), ct);
            }
            catch (ForbiddenException)
            {
                return BadRequest();
            }
            catch (RuleViolationException ex) when (ex.Code == "da_thanh_toan" || ex.Code == "lenh_nap_da_chot")
            {
                return NoContent();
            }

            return NoContent();
        }

        private async Task<string> DocBodyAsync(CancellationToken ct)
        {
            using (StreamReader r = new StreamReader(Request.Body, Encoding.UTF8))
            {
                return await r.ReadToEndAsync(ct);
            }
        }

        public static Dictionary<string, string>? DocJsonPhang(string body)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        return null;
                    }

                    Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (JsonProperty p in doc.RootElement.EnumerateObject())
                    {
                        d[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : p.Value.GetRawText();
                    }

                    return d;
                }
            }
            catch (JsonException)
            {
                return null;
            }
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

    /// <summary>Phan hoi IPN theo quy uoc VNPay (ten truong viet hoa dung nhu tai lieu).</summary>
    public sealed class VnPayIpnResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("RspCode")]
        public required string RspCode { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("Message")]
        public required string Message { get; init; }
    }

    /// <summary>
    /// CHI MOI TRUONG DEV: gia lap trang thanh toan cua cong sandbox. Bam "tra tien" thi cong gia lap
    /// ky va goi webhook — di qua DUNG duong kiem chu ky nhu cong that.
    /// </summary>
    [ApiController]
    [Route("payments/sandbox")]
    [AllowAnonymous]
    public sealed class SandboxCheckoutController : ControllerBase
    {
        private static readonly JsonSerializerOptions JsonWebhook = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        private readonly DepositService _service;
        private readonly IServiceProvider _sp;
        private readonly IWebHostEnvironment _env;

        public SandboxCheckoutController(DepositService service, IServiceProvider sp, IWebHostEnvironment env)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            if (sp == null)
            {
                throw new ArgumentNullException(nameof(sp));
            }

            if (env == null)
            {
                throw new ArgumentNullException(nameof(env));
            }

            _service = service;
            _sp = sp;
            _env = env;
        }

        /// <summary>POST /payments/sandbox/checkout/{intentId}/pay?amount=</summary>
        [HttpPost("checkout/{intentId:guid}/pay")]
        public async Task<IActionResult> TraTien(Guid intentId, [FromQuery] long amount, CancellationToken ct)
        {
            SandboxPaymentProvider? sandbox = _sp.GetService<SandboxPaymentProvider>();
            if (!_env.IsDevelopment() || sandbox == null)
            {
                return NotFound();
            }

            SandboxWebhook w = new SandboxWebhook
            {
                IntentId = intentId,
                AmountVnd = amount,
                Status = "success",
                ProviderTxnId = "SBX-D-" + intentId.ToString("N"),
            };

            string body = JsonSerializer.Serialize(w, JsonWebhook);
            await _service.XuLyThongBaoAsync(sandbox, sandbox.DocWebhook(body, sandbox.Ky(body)), Guid.CreateVersion7(), ct);
            return Ok(new { paid = true, providerTxnId = w.ProviderTxnId });
        }
    }
}
