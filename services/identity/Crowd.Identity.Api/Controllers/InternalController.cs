using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Internal;
using Crowd.BuildingBlocks.Settings;
using Crowd.Identity.Api.Dtos;
using Crowd.Identity.Api.Entities;
using Crowd.Identity.Api.Persistence;
using Crowd.Identity.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Identity.Api.Controllers
{
    /// <summary>
    /// API NOI BO (khoa X-Internal-Key, gateway khong dinh tuyen /internal).
    /// Gateway goi luc khoi dong de nap lai cac moc thu hoi phien con tac dung — sau do theo doi
    /// qua event user.sessions_revoked. ledger-svc / link-svc goi users/lookup.
    /// </summary>
    [ApiController]
    [Route("internal")]
    [InternalApi]
    public sealed class InternalController : ControllerBase
    {
        private readonly IdentityDbContext _db;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;
        private readonly TraCuuNguoiDungService _traCuu;

        public InternalController(IdentityDbContext db, ISettings settings, TimeProvider clock, TraCuuNguoiDungService traCuu)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            if (traCuu == null)
            {
                throw new ArgumentNullException(nameof(traCuu));
            }

            _db = db;
            _settings = settings;
            _clock = clock;
            _traCuu = traCuu;
        }

        /// <summary>
        /// GET /internal/session-revocations — moc thu hoi con tac dung: moc cu hon tuoi access token
        /// thi moi token phat truoc no da tu het han, gateway khong can biet nua.
        /// </summary>
        [HttpGet("session-revocations")]
        public async Task<IActionResult> MocThuHoi(CancellationToken ct)
        {
            DateTimeOffset tu = _clock.GetUtcNow() - _settings.ThoiGian(SettingKeys.IdentityAccessTokenLifetime);
            List<SessionRevocationDto> ds = await _db.MocThuHoiPhiens.AsNoTracking()
                .Where(m => m.RevokedAt > tu)
                .Select(m => new SessionRevocationDto { UserId = m.UserId, RevokedAt = m.RevokedAt })
                .ToListAsync(ct);
            return Ok(ds);
        }

        /// <summary>
        /// POST /internal/users/lookup — ledger-svc kiem khoa luc rut tien; link-svc kiem luc dang ky luc
        /// nhap ma gioi thieu (project-svc hoi cung du lieu qua gRPC IdentityData). Khong co trong ket qua =
        /// khong ton tai; qua 1000 id + email + username → 400. Email tra ve nam trong mang noi bo.
        /// </summary>
        [HttpPost("users/lookup")]
        public async Task<IActionResult> TraCuu([FromBody] UserLookupRequest body, CancellationToken ct)
        {
            if (body == null)
            {
                return BadRequest();
            }

            List<UserLookupDto> ds = await _traCuu.TraCuuAsync(
                body.UserIds ?? new List<Guid>(), body.Emails ?? new List<string>(), body.Usernames ?? new List<string>(), ct);
            return Ok(ds);
        }

        /// <summary>
        /// GET /internal/businesses/{id} — ho so doanh nghiep (KYB) de ledger in len hoa don VAT (FB-05).
        /// Hoi CHU du lieu dung luc xuat hoa don va CHUP vao hoa don (doi ten sau nay khong sua hoa don cu).
        /// </summary>
        [HttpGet("businesses/{businessId:guid}")]
        public async Task<IActionResult> DoanhNghiep(Guid businessId, CancellationToken ct)
        {
            BusinessProfile? p = await _db.BusinessProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == businessId, ct);
            Crowd.Identity.Api.Entities.User? u = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == businessId, ct);
            if (p == null || u == null)
            {
                return NotFound();
            }

            return Ok(new BusinessInternalDto
            {
                BusinessId = p.UserId,
                Status = Crowd.Identity.Api.Services.KybService.TenTrangThai(p.Status),
                CompanyName = p.CompanyName,
                TaxCode = p.TaxCode,
                Address = p.Address,
                Email = u.Email,
            });
        }
    }
}
