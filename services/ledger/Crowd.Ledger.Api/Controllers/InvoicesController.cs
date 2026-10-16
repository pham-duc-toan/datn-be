using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Messaging;
using Crowd.Ledger.Api.Dtos;
using Crowd.Ledger.Api.Helpers;
using Crowd.Ledger.Api.Services;
using Crowd.Ledger.Domain.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crowd.Ledger.Api.Controllers
{
    /// <summary>Hoa don GTGT phi nen tang (FB-05).</summary>
    [ApiController]
    [Route("ledger/invoices")]
    [Authorize]
    public sealed class InvoicesController : ControllerBase
    {
        private readonly InvoiceService _service;

        public InvoicesController(InvoiceService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            _service = service;
        }

        /// <summary>GET /ledger/invoices — hoa don cua minh (moi nhat truoc).</summary>
        [HttpGet]
        [Authorize(Roles = CrowdRoles.Business)]
        public async Task<IActionResult> CuaToi(CancellationToken ct)
        {
            return Ok(await _service.CuaToiAsync(DoanhNghiep(), ct));
        }

        /// <summary>POST /ledger/invoices { period: "2026-09" } — xuat hoa don thang da ket thuc (da co thi tra lai).</summary>
        [HttpPost]
        [Authorize(Roles = CrowdRoles.Business)]
        public async Task<IActionResult> Xuat([FromBody] IssueInvoiceRequest body, CancellationToken ct)
        {
            return Ok(await _service.XuatAsync(body == null ? null : body.Period, DoanhNghiep(), ct));
        }

        /// <summary>GET /ledger/invoices/preview?period=2026-10 — tam tinh (ke ca thang dang chay), khong luu.</summary>
        [HttpGet("preview")]
        [Authorize(Roles = CrowdRoles.Business)]
        public async Task<IActionResult> TamTinh([FromQuery] string? period, CancellationToken ct)
        {
            return Ok(await _service.TamTinhAsync(period, DoanhNghiep(), ct));
        }

        /// <summary>GET /ledger/invoices/admin?period=&amp;page=&amp;pageSize=</summary>
        [HttpGet("admin")]
        [Authorize(Roles = CrowdRoles.Admin)]
        public async Task<IActionResult> Admin([FromQuery] string? period, [FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct)
        {
            return Ok(await _service.AdminDanhSachAsync(period, page, pageSize, ct));
        }

        /// <summary>GET /ledger/invoices/{id} — chu hoa don hoac admin.</summary>
        [HttpGet("{id:guid}")]
        [Authorize(Roles = CrowdRoles.Business + "," + CrowdRoles.Admin)]
        public async Task<IActionResult> ChiTiet(Guid id, CancellationToken ct)
        {
            return Ok(InvoiceService.TaoResponse(await _service.LayAsync(id, NguoiXem(), ct)));
        }

        /// <summary>GET /ledger/invoices/{id}/html — ban the hien in duoc.</summary>
        [HttpGet("{id:guid}/html")]
        [Authorize(Roles = CrowdRoles.Business + "," + CrowdRoles.Admin)]
        public async Task<IActionResult> Html(Guid id, CancellationToken ct)
        {
            Invoice h = await _service.LayAsync(id, NguoiXem(), ct);
            return Content(HoaDonRenderer.Html(h), "text/html; charset=utf-8");
        }

        /// <summary>GET /ledger/invoices/{id}/xml — du lieu hoa don dien tu (cau truc Thong tu 78).</summary>
        [HttpGet("{id:guid}/xml")]
        [Authorize(Roles = CrowdRoles.Business + "," + CrowdRoles.Admin)]
        public async Task<IActionResult> Xml(Guid id, CancellationToken ct)
        {
            Invoice h = await _service.LayAsync(id, NguoiXem(), ct);
            Response.Headers.ContentDisposition = "attachment; filename=\"hoa-don-" + h.Serial + "-" + h.SoHienThi + ".xml\"";
            return Content(HoaDonRenderer.Xml(h), "application/xml; charset=utf-8");
        }

        private Caller DoanhNghiep()
        {
            return Caller.TuHttp(HttpContext, ActorRole.Business);
        }

        private Caller NguoiXem()
        {
            return Caller.TuHttp(HttpContext, User.IsInRole(CrowdRoles.Admin) ? ActorRole.Admin : ActorRole.Business);
        }
    }
}
