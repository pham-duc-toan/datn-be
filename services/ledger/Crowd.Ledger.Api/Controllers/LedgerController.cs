using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Messaging;
using Crowd.Ledger.Api.Dtos;
using Crowd.Ledger.Api.Services;
using Crowd.Ledger.Domain.Withdrawals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crowd.Ledger.Api.Controllers
{
    /// <summary>
    /// Vi va doi soat. KHONG co endpoint nao "cong tien" hay "sua so du": tien chi
    /// di chuyen do SU VIEC nghiep vu (event) hoac do chinh nguoi dung rut tien cua
    /// minh. Moi dong tien khac khong co cua vao tu HTTP.
    /// </summary>
    [ApiController]
    [Route("ledger")]
    [Authorize]
    public sealed class LedgerController : ControllerBase
    {
        private readonly WalletService _wallet;
        private readonly ReconciliationService _recon;
        private readonly WithdrawalApprovalService _duyetRut;

        public LedgerController(WalletService wallet, ReconciliationService recon, WithdrawalApprovalService duyetRut)
        {
            if (duyetRut == null)
            {
                throw new ArgumentNullException(nameof(duyetRut));
            }

            _duyetRut = duyetRut;

            if (wallet == null)
            {
                throw new ArgumentNullException(nameof(wallet));
            }

            if (recon == null)
            {
                throw new ArgumentNullException(nameof(recon));
            }

            _wallet = wallet;
            _recon = recon;
        }

        /// <summary>GET /ledger/me/balance (FB-04, FL-10)</summary>
        [HttpGet("me/balance")]
        public async Task<IActionResult> SoDu(CancellationToken ct)
        {
            return Ok(await _wallet.SoDuAsync(Caller.TuHttp(HttpContext, ActorRole.Labeler), ct));
        }

        /// <summary>GET /ledger/me/transactions (FB-05, FL-11)</summary>
        [HttpGet("me/transactions")]
        public async Task<IActionResult> LichSu([FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct)
        {
            return Ok(await _wallet.LichSuAsync(Caller.TuHttp(HttpContext, ActorRole.Labeler), page, pageSize, ct));
        }

        /// <summary>POST /ledger/withdrawals — header Idempotency-Key BAT BUOC (docs 3.3).</summary>
        [HttpPost("withdrawals")]
        [Authorize(Roles = CrowdRoles.Labeler)]
        public async Task<IActionResult> Rut(
            [FromBody] WithdrawRequest body,
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
            CancellationToken ct)
        {
            WithdrawalResponse w = await _wallet.RutAsync(body, idempotencyKey, Caller.TuHttp(HttpContext, ActorRole.Labeler), ct);
            return Ok(w);
        }

        [HttpGet("withdrawals/mine")]
        [Authorize(Roles = CrowdRoles.Labeler)]
        public async Task<IActionResult> LenhRut(CancellationToken ct)
        {
            return Ok(await _wallet.LenhRutCuaToiAsync(Caller.TuHttp(HttpContext, ActorRole.Labeler), ct));
        }

        /// <summary>GET /ledger/admin/reconciliation — doi soat (FM-05).</summary>
        [HttpGet("admin/reconciliation")]
        [Authorize(Roles = CrowdRoles.Admin)]
        public async Task<IActionResult> DoiSoat(CancellationToken ct)
        {
            return Ok(await _recon.DoiSoatAsync(ct));
        }

        /// <summary>GET /ledger/admin/withdrawals?state=pendingApproval — hang doi duyet rut tien.</summary>
        [HttpGet("admin/withdrawals")]
        [Authorize(Roles = CrowdRoles.Admin)]
        public async Task<IActionResult> HangDoiRut(
            [FromQuery] WithdrawalState? state, [FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct)
        {
            return Ok(await _duyetRut.DanhSachAsync(state, page, pageSize, ct));
        }

        /// <summary>POST /ledger/admin/withdrawals/{id}/approve — duyet, gui payment-svc chuyen khoan.</summary>
        [HttpPost("admin/withdrawals/{id:guid}/approve")]
        [Authorize(Roles = CrowdRoles.Admin)]
        public async Task<IActionResult> DuyetRut(Guid id, CancellationToken ct)
        {
            return Ok(await _duyetRut.DuyetAsync(id, Caller.TuHttp(HttpContext, ActorRole.Admin), ct));
        }

        /// <summary>POST /ledger/admin/withdrawals/{id}/reject {"reason": "..."} — tien ve lai vi labeler.</summary>
        [HttpPost("admin/withdrawals/{id:guid}/reject")]
        [Authorize(Roles = CrowdRoles.Admin)]
        public async Task<IActionResult> TuChoiRut(Guid id, [FromBody] RejectWithdrawalRequest? body, CancellationToken ct)
        {
            string? lyDo = body == null ? null : body.Reason;
            return Ok(await _duyetRut.TuChoiAsync(id, lyDo, Caller.TuHttp(HttpContext, ActorRole.Admin), ct));
        }
    }
}
