using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Settings;
using Crowd.Identity.Api.Dtos;
using Crowd.Identity.Api.Entities;
using Crowd.Identity.Api.Exceptions;
using Crowd.Identity.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Crowd.Identity.Api.Services
{
    /// <summary>
    /// Tra cuu nguoi dung cho service khac (vai tro, khoa, cap do, uy tin, xac minh): project-svc qua gRPC
    /// IdentityData (moi / tham gia du an, mo bai — kien truc 3.24), ledger-svc / link-svc qua REST
    /// POST /internal/users/lookup. Khong co trong ket qua = khong ton tai.
    /// </summary>
    public sealed class TraCuuNguoiDungService
    {
        /// <summary>Tran so id + email + username moi lan tra cuu (noi goi chia lo).</summary>
        public const int ToiDa = 1000;

        private readonly IdentityDbContext _db;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;
        private readonly NguonUyTin _uyTin;
        private readonly ILogger<TraCuuNguoiDungService> _logger;

        public TraCuuNguoiDungService(IdentityDbContext db, ISettings settings, TimeProvider clock, NguonUyTin uyTin, ILogger<TraCuuNguoiDungService> logger)
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
            if (uyTin == null)
            {
                throw new ArgumentNullException(nameof(uyTin));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _clock = clock;
            _uyTin = uyTin;
            _logger = logger;
        }

        public async Task<List<UserLookupDto>> TraCuuAsync(
            IEnumerable<Guid> userIds, IEnumerable<string> emails, IEnumerable<string> usernames, CancellationToken ct)
        {
            if (userIds == null)
            {
                throw new ArgumentNullException(nameof(userIds));
            }

            if (emails == null)
            {
                throw new ArgumentNullException(nameof(emails));
            }

            if (usernames == null)
            {
                throw new ArgumentNullException(nameof(usernames));
            }

            List<Guid> ids = userIds.Distinct().ToList();
            List<string> email = emails.Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => User.ChuanHoaEmail(e)).Distinct(StringComparer.Ordinal).ToList();
            List<string> ten = usernames.Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => User.ChuanHoaUsername(x)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count + email.Count + ten.Count > ToiDa)
            {
                throw new InvalidValueException("qua_nhieu", "Toi da " + ToiDa + " id + email + username moi lan tra cuu.");
            }

            if (ids.Count + email.Count + ten.Count == 0)
            {
                return new List<UserLookupDto>();
            }

            List<User> users = await _db.Users.AsNoTracking()
                .Where(u => ids.Contains(u.Id) || email.Contains(u.Email) || ten.Contains(u.Username))
                .ToListAsync(ct);

            List<Guid> tim = users.Select(u => u.Id).ToList();
            Dictionary<Guid, ThongKeLabeler> thongKe = await _db.ThongKeLabelers.AsNoTracking()
                .Where(t => tim.Contains(t.UserId))
                .ToDictionaryAsync(t => t.UserId, ct);

            // Uy tin doc thang view v_reputations cua quality (3.32). Quality tam khong tra loi → coi nhu chua co uy tin
            // (null): dieu kien uy tin toi thieu cua du an van TU CHOI, khong bao gio cho qua nham; tra cuu cho ledger /
            // link (khong can uy tin) van chay.
            Dictionary<Guid, int> uyTin;
            try
            {
                uyTin = await _uyTin.LayAsync(tim, ct);
            }
            catch (NpgsqlException ex)
            {
                _logger.LogWarning(ex, "Chua doc duoc uy tin tu quality-svc — tra cuu tra uy tin rong");
                uyTin = new Dictionary<Guid, int>();
            }

            // Xac minh danh tinh / du tuoi (FP-05, muc 2.9): project kiem khi moi vao du an rieng tu / nhay cam.
            Dictionary<Guid, XacMinhDanhTinh> xacMinh = await _db.XacMinhDanhTinhs.AsNoTracking()
                .Where(x => tim.Contains(x.UserId) && x.Status == KybStatus.Verified)
                .ToDictionaryAsync(x => x.UserId, ct);
            int tuoi = _settings.SoNguyen(SettingKeys.IdentityAdultAge);
            DateOnly homNay = XacMinhDanhTinhService.HomNayVietNam(_clock.GetUtcNow());

            List<UserLookupDto> ds = new List<UserLookupDto>();
            foreach (User u in users)
            {
                ThongKeLabeler? tk;
                thongKe.TryGetValue(u.Id, out tk);
                XacMinhDanhTinh? xm;
                xacMinh.TryGetValue(u.Id, out xm);
                ds.Add(new UserLookupDto
                {
                    UserId = u.Id,
                    Email = u.Email,
                    Username = u.Username,
                    DisplayName = u.DisplayName,
                    Roles = u.Roles,
                    Locked = u.Status == UserStatus.Blocked,
                    RegisteredAt = u.CreatedAt,
                    Level = tk == null ? 1 : tk.Level,
                    Reputation = uyTin.TryGetValue(u.Id, out int diem) ? diem : null,
                    IdentityVerified = xm != null,
                    Adult = xm != null && xm.DuTuoi(tuoi, homNay),
                    Domains = u.Domains,
                    Languages = u.Languages,
                    Modalities = u.Modalities,
                });
            }

            return ds;
        }
    }
}
