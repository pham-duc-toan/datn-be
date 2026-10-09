using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Security;
using Crowd.Link.Api.Dtos;
using Crowd.Link.Api.Entities;
using Crowd.Link.Api.Helpers;
using Crowd.Link.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Crowd.Link.Api.Controllers
{
    /// <summary>Link rut gon cua nguoi chia se (FS-01→03, 05, 06), chien dich, API key, gioi thieu.</summary>
    [ApiController]
    [Route("links")]
    public sealed class LinksController : ControllerBase
    {
        private readonly LinkService _links;
        private readonly ReferralService _referrals;
        private readonly ModerationService _moderation;
        private readonly PrivacyOptions _privacy;

        public LinksController(LinkService links, ReferralService referrals, ModerationService moderation, IOptions<PrivacyOptions> privacy)
        {
            if (links == null)
            {
                throw new ArgumentNullException(nameof(links));
            }

            if (referrals == null)
            {
                throw new ArgumentNullException(nameof(referrals));
            }

            if (moderation == null)
            {
                throw new ArgumentNullException(nameof(moderation));
            }

            if (privacy == null)
            {
                throw new ArgumentNullException(nameof(privacy));
            }

            _links = links;
            _referrals = referrals;
            _moderation = moderation;
            _privacy = privacy.Value;
        }

        /// <summary>POST /links — token sharer hoac header X-Api-Key.</summary>
        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Tao([FromBody] CreateLinkRequest body, CancellationToken ct)
        {
            Caller c = await NguoiGoi.SharerAsync(HttpContext, _links, null, ct);
            LinkResponse l = await _links.TaoAsync(body, c.LayUserId(), NguoiGoi.IpHash(HttpContext, _privacy.IpSalt), ct);
            return Created("/links/" + l.Id, l);
        }

        /// <summary>POST /links/bulk {"urls":[...]} — moi URL mot dong ket qua (FS-03).</summary>
        [HttpPost("bulk")]
        [AllowAnonymous]
        public async Task<IActionResult> TaoHangLoat([FromBody] BulkCreateLinksRequest body, CancellationToken ct)
        {
            Caller c = await NguoiGoi.SharerAsync(HttpContext, _links, null, ct);
            return Ok(await _links.TaoHangLoatAsync(body, c.LayUserId(), NguoiGoi.IpHash(HttpContext, _privacy.IpSalt), ct));
        }

        /// <summary>
        /// GET /links/quick?api=KEY&amp;url=...&amp;alias=... — Quick Link (FS-02): dan vao
        /// trinh duyet / goi tu script, tra ve CHUOI link rut gon (text/plain).
        /// </summary>
        [HttpGet("quick")]
        [AllowAnonymous]
        public async Task<IActionResult> Nhanh([FromQuery] string? api, [FromQuery] string? url, [FromQuery] string? alias, CancellationToken ct)
        {
            Caller c = await NguoiGoi.SharerAsync(HttpContext, _links, api, ct);
            LinkResponse l = await _links.TaoAsync(new CreateLinkRequest { Url = url, Alias = alias }, c.LayUserId(), NguoiGoi.IpHash(HttpContext, _privacy.IpSalt), ct);
            return Content(l.ShortUrl, "text/plain");
        }

        [HttpGet]
        [Authorize(Roles = CrowdRoles.Sharer)]
        public async Task<IActionResult> DanhSach(
            [FromQuery] Guid? campaignId, [FromQuery] LinkStatus? status, [FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct)
        {
            Caller c = Caller.TuHttp(HttpContext, ActorRole.Sharer);
            return Ok(await _links.DanhSachAsync(c.LayUserId(), campaignId, status, page, pageSize, ct));
        }

        [HttpGet("{id:guid}")]
        [Authorize(Roles = CrowdRoles.Sharer)]
        public async Task<IActionResult> Xem(Guid id, CancellationToken ct)
        {
            return Ok(await _links.XemAsync(id, Caller.TuHttp(HttpContext, ActorRole.Sharer).LayUserId(), ct));
        }

        /// <summary>PUT /links/{id} — doi mat khau / het han / chien dich (link dich khong doi duoc).</summary>
        [HttpPut("{id:guid}")]
        [Authorize(Roles = CrowdRoles.Sharer)]
        public async Task<IActionResult> Sua(Guid id, [FromBody] UpdateLinkRequest body, CancellationToken ct)
        {
            return Ok(await _links.CapNhatAsync(id, body, Caller.TuHttp(HttpContext, ActorRole.Sharer), ct));
        }

        [HttpDelete("{id:guid}")]
        [Authorize(Roles = CrowdRoles.Sharer)]
        public async Task<IActionResult> Xoa(Guid id, CancellationToken ct)
        {
            await _links.XoaAsync(id, Caller.TuHttp(HttpContext, ActorRole.Sharer), ct);
            return NoContent();
        }

        [HttpPost("campaigns")]
        [Authorize(Roles = CrowdRoles.Sharer)]
        public async Task<IActionResult> TaoChienDich([FromBody] CreateCampaignRequest body, CancellationToken ct)
        {
            return Ok(await _links.TaoChienDichAsync(body, Caller.TuHttp(HttpContext, ActorRole.Sharer).LayUserId(), ct));
        }

        [HttpGet("campaigns")]
        [Authorize(Roles = CrowdRoles.Sharer)]
        public async Task<IActionResult> DanhSachChienDich(CancellationToken ct)
        {
            return Ok(await _links.DanhSachChienDichAsync(Caller.TuHttp(HttpContext, ActorRole.Sharer).LayUserId(), ct));
        }

        /// <summary>POST /links/api-key — tao (hoac tao lai) API key. Key day du chi tra ve MOT lan nay.</summary>
        [HttpPost("api-key")]
        [Authorize(Roles = CrowdRoles.Sharer)]
        public async Task<IActionResult> TaoApiKey(CancellationToken ct)
        {
            return Ok(await _links.TaoApiKeyAsync(Caller.TuHttp(HttpContext, ActorRole.Sharer).LayUserId(), ct));
        }

        [HttpGet("api-key")]
        [Authorize(Roles = CrowdRoles.Sharer)]
        public async Task<IActionResult> XemApiKey(CancellationToken ct)
        {
            ApiKeyResponse? k = await _links.XemApiKeyAsync(Caller.TuHttp(HttpContext, ActorRole.Sharer).LayUserId(), ct);
            if (k == null)
            {
                return NotFound();
            }

            return Ok(k);
        }

        /// <summary>GET /links/referrals/me — ma gioi thieu cua toi + so nguoi da moi.</summary>
        [HttpGet("referrals/me")]
        [Authorize]
        public async Task<IActionResult> GioiThieuCuaToi(CancellationToken ct)
        {
            return Ok(await _referrals.CuaToiAsync(Caller.TuHttp(HttpContext, ActorRole.Sharer).LayUserId(), ct));
        }

        /// <summary>POST /links/referrals/claim {"code"} — tai khoan moi nhap ma nguoi gioi thieu.</summary>
        [HttpPost("referrals/claim")]
        [Authorize]
        public async Task<IActionResult> NhapMa([FromBody] ClaimReferralRequest body, CancellationToken ct)
        {
            await _referrals.NhapMaAsync(body, Caller.TuHttp(HttpContext, ActorRole.Sharer), ct);
            return NoContent();
        }

        /// <summary>POST /links/r/{code}/report {"reason"} — nut "bao cao vi pham" o trang vuot link (khach vang lai).</summary>
        [HttpPost("r/{code}/report")]
        [AllowAnonymous]
        public async Task<IActionResult> BaoCao(string code, [FromBody] ReportLinkRequest body, CancellationToken ct)
        {
            await _moderation.BaoCaoAsync(code, body == null ? null : body.Reason, NguoiGoi.IpHash(HttpContext, _privacy.IpSalt), ct);
            return Accepted();
        }
    }

    /// <summary>Kiem duyet link (admin).</summary>
    [ApiController]
    [Route("links/admin")]
    [Authorize(Roles = CrowdRoles.Admin)]
    public sealed class AdminLinksController : ControllerBase
    {
        private readonly ModerationService _moderation;

        public AdminLinksController(ModerationService moderation)
        {
            if (moderation == null)
            {
                throw new ArgumentNullException(nameof(moderation));
            }

            _moderation = moderation;
        }

        /// <summary>GET /links/admin/review-queue — link bi bao cao vuot nguong.</summary>
        [HttpGet("review-queue")]
        public async Task<IActionResult> HangDoi([FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct)
        {
            return Ok(await _moderation.HangDoiAsync(page, pageSize, ct));
        }

        /// <summary>POST /links/admin/{id}/disable {"reason","withholdRevenue"} — vo hieu hoa vi vi pham.</summary>
        [HttpPost("{id:guid}/disable")]
        public async Task<IActionResult> VoHieuHoa(Guid id, [FromBody] DisableLinkRequest? body, CancellationToken ct)
        {
            return Ok(await _moderation.VoHieuHoaAsync(id, body, Caller.TuHttp(HttpContext, ActorRole.Admin), ct));
        }

        /// <summary>POST /links/admin/{id}/dismiss-reports — xem xong, khong vi pham.</summary>
        [HttpPost("{id:guid}/dismiss-reports")]
        public async Task<IActionResult> BoQua(Guid id, CancellationToken ct)
        {
            return Ok(await _moderation.BoQuaBaoCaoAsync(id, ct));
        }

        [HttpGet("blocked-domains")]
        public async Task<IActionResult> TenMienChan(CancellationToken ct)
        {
            IReadOnlyList<BlockedDomainResponse> ds = await _moderation.DanhSachTenMienChanAsync(ct);
            return Ok(ds);
        }

        /// <summary>POST /links/admin/blocked-domains {"domain","reason"} — link dang chay toi ten mien do bi vo hieu hoa ngay.</summary>
        [HttpPost("blocked-domains")]
        public async Task<IActionResult> Chan([FromBody] BlockDomainRequest body, CancellationToken ct)
        {
            return Ok(await _moderation.ChanTenMienAsync(body, Caller.TuHttp(HttpContext, ActorRole.Admin), ct));
        }

        [HttpDelete("blocked-domains/{domain}")]
        public async Task<IActionResult> BoChan(string domain, CancellationToken ct)
        {
            await _moderation.BoChanTenMienAsync(domain, ct);
            return NoContent();
        }
    }
}
