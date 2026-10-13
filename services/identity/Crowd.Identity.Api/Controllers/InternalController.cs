using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Internal;
using Crowd.BuildingBlocks.Settings;
using Crowd.Identity.Api.Dtos;
using Crowd.Identity.Api.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Identity.Api.Controllers
{
    /// <summary>
    /// API NOI BO (khoa X-Internal-Key, gateway khong dinh tuyen /internal).
    /// Gateway goi luc khoi dong de nap lai cac moc thu hoi phien con tac dung — sau do theo doi
    /// qua event user.sessions_revoked.
    /// </summary>
    [ApiController]
    [Route("internal")]
    [InternalApi]
    public sealed class InternalController : ControllerBase
    {
        private readonly IdentityDbContext _db;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;

        public InternalController(IdentityDbContext db, ISettings settings, TimeProvider clock)
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

            _db = db;
            _settings = settings;
            _clock = clock;
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
    }
}
