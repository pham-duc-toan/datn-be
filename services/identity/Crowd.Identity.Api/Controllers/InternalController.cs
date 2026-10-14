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
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Identity.Api.Controllers
{
    /// <summary>
    /// API NOI BO (khoa X-Internal-Key, gateway khong dinh tuyen /internal).
    /// Gateway goi luc khoi dong de nap lai cac moc thu hoi phien con tac dung — sau do theo doi
    /// qua event user.sessions_revoked. project-svc goi users/lookup khi moi / nhan nguoi vao du an.
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

        /// <summary>
        /// POST /internal/users/lookup — project-svc tra cuu nguoi duoc moi (theo email / id) va
        /// kiem dieu kien tham gia du an (vai tro, khoa, cap do, uy tin). Khong co trong ket qua =
        /// khong ton tai. Email tra ve nam trong mang noi bo, khong di qua gateway.
        /// </summary>
        [HttpPost("users/lookup")]
        public async Task<IActionResult> TraCuu([FromBody] UserLookupRequest body, CancellationToken ct)
        {
            if (body == null)
            {
                return BadRequest();
            }

            List<Guid> ids = body.UserIds == null ? new List<Guid>() : body.UserIds.Distinct().ToList();
            List<string> emails = new List<string>();
            if (body.Emails != null)
            {
                foreach (string e in body.Emails)
                {
                    if (!string.IsNullOrWhiteSpace(e))
                    {
                        emails.Add(Crowd.Identity.Api.Entities.User.ChuanHoaEmail(e));
                    }
                }
            }

            emails = emails.Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count + emails.Count > TraCuuToiDa)
            {
                return BadRequest();
            }

            List<Crowd.Identity.Api.Entities.User> users = await _db.Users.AsNoTracking()
                .Where(u => ids.Contains(u.Id) || emails.Contains(u.Email))
                .ToListAsync(ct);

            List<Guid> tim = users.Select(u => u.Id).ToList();
            Dictionary<Guid, ThongKeLabeler> thongKe = await _db.ThongKeLabelers.AsNoTracking()
                .Where(t => tim.Contains(t.UserId))
                .ToDictionaryAsync(t => t.UserId, ct);

            List<UserLookupDto> ds = new List<UserLookupDto>();
            foreach (Crowd.Identity.Api.Entities.User u in users)
            {
                ThongKeLabeler? tk;
                thongKe.TryGetValue(u.Id, out tk);
                ds.Add(new UserLookupDto
                {
                    UserId = u.Id,
                    Email = u.Email,
                    DisplayName = u.DisplayName,
                    Roles = u.Roles,
                    Locked = u.Status == UserStatus.Blocked,
                    Level = tk == null ? 1 : tk.Level,
                    Reputation = tk == null ? null : tk.Reputation,
                });
            }

            return Ok(ds);
        }

        /// <summary>Tran so id + email moi lan tra cuu (project-svc chia lo theo setting moi nhieu nguoi).</summary>
        private const int TraCuuToiDa = 1000;
    }
}
