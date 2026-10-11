using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.Contracts.Identity;
using Crowd.Identity.Api.Dtos;
using Crowd.Identity.Api.Entities;
using Crowd.Identity.Api.Exceptions;
using Crowd.Identity.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Identity.Api.Services
{
    /// <summary>
    /// Admin khoa / mo tai khoan (FM-01). Khoa:
    ///   - dang nhap bi tu choi (AuthService da kiem UserStatus.Blocked);
    ///   - THU HOI moi refresh token → khong lam moi duoc phien; access token con song toi
    ///     het han (15 phut) — cac service khac chan som hon qua user.blocked;
    ///   - phat user.blocked: task-svc thu hoi lease + chan nhan task, ledger chan rut, notification bao.
    /// Khong cho khoa admin (tranh tu khoa het quyen quan tri).
    /// </summary>
    public sealed class TaiKhoanAdminService
    {
        private readonly IdentityDbContext _db;
        private readonly IOutboxWriter _outbox;
        private readonly TimeProvider _clock;

        public TaiKhoanAdminService(IdentityDbContext db, IOutboxWriter outbox, TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (outbox == null)
            {
                throw new ArgumentNullException(nameof(outbox));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _db = db;
            _outbox = outbox;
            _clock = clock;
        }

        public async Task<PagedResponse<AdminUserResponse>> DanhSachAsync(
            string? tim, string? vaiTro, string? trangThai, int page, int pageSize, CancellationToken ct)
        {
            int trang = page < 1 ? 1 : page;
            int co = pageSize < 1 || pageSize > 100 ? 20 : pageSize;

            IQueryable<User> q = _db.Users.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(tim))
            {
                // ILIKE cua Postgres: khong phan biet hoa thuong, chay trong DB. Escape ky tu dai dien.
                string mau = "%" + tim.Trim().Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
                q = q.Where(u => EF.Functions.ILike(u.Email, mau) || EF.Functions.ILike(u.DisplayName, mau));
            }

            if (!string.IsNullOrWhiteSpace(vaiTro))
            {
                string v = vaiTro.Trim();
                q = q.Where(u => EF.Property<List<string>>(u, "_roles").Contains(v));
            }

            if (!string.IsNullOrWhiteSpace(trangThai))
            {
                UserStatus s;
                if (!Enum.TryParse(trangThai, true, out s))
                {
                    throw new InvalidValueException("trang_thai_khong_hop_le", "status: active | blocked.");
                }

                q = q.Where(u => u.Status == s);
            }

            int tong = await q.CountAsync(ct);
            List<User> ds = await q.OrderByDescending(u => u.CreatedAt).Skip((trang - 1) * co).Take(co).ToListAsync(ct);

            List<Guid> ids = ds.Select(u => u.Id).ToList();
            Dictionary<Guid, KybStatus> kyb = await _db.BusinessProfiles.AsNoTracking()
                .Where(p => ids.Contains(p.UserId))
                .ToDictionaryAsync(p => p.UserId, p => p.Status, ct);

            List<AdminUserResponse> items = new List<AdminUserResponse>();
            foreach (User u in ds)
            {
                items.Add(TaoResponse(u, kyb));
            }

            return new PagedResponse<AdminUserResponse> { Items = items, Page = trang, PageSize = co, Total = tong };
        }

        public async Task<AdminUserResponse> KhoaAsync(Guid userId, string? lyDo, Caller caller, CancellationToken ct)
        {
            string ly = (lyDo ?? string.Empty).Trim();
            if (ly.Length == 0 || ly.Length > 1000)
            {
                throw new InvalidValueException("thieu_ly_do", "Khoa tai khoan phai ghi ly do (toi da 1000 ky tu).");
            }

            User u = await LayAsync(userId, ct);
            if (u.Roles.Contains("admin"))
            {
                throw new RuleViolationException("khong_khoa_admin", "Khong khoa duoc tai khoan admin.");
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            if (u.Khoa(ly, bayGio))
            {
                // Thu hoi moi phien dang nhap: refresh token khong con dung duoc.
                await _db.RefreshTokens
                    .Where(t => t.UserId == userId && t.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, bayGio), ct);

                Phat(caller, new UserBlocked { UserId = userId, Reason = ly });
                await _db.SaveChangesAsync(ct);
            }

            return await TaoResponseAsync(u, ct);
        }

        public async Task<AdminUserResponse> MoKhoaAsync(Guid userId, Caller caller, CancellationToken ct)
        {
            User u = await LayAsync(userId, ct);
            if (u.MoKhoa())
            {
                Phat(caller, new UserUnblocked { UserId = userId });
                await _db.SaveChangesAsync(ct);
            }

            return await TaoResponseAsync(u, ct);
        }

        private async Task<User> LayAsync(Guid userId, CancellationToken ct)
        {
            User? u = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId, ct);
            if (u == null)
            {
                throw new NotFoundException("Khong tim thay tai khoan.");
            }

            return u;
        }

        private void Phat<TPayload>(Caller caller, TPayload payload)
            where TPayload : class, IEventPayload
        {
            _outbox.Enqueue(EventEnvelope.Create(
                producer: "identity-svc",
                correlationId: caller.CorrelationId,
                payload: payload,
                causationId: caller.CausationId,
                actor: caller.TaoActor()));
        }

        private async Task<AdminUserResponse> TaoResponseAsync(User u, CancellationToken ct)
        {
            Dictionary<Guid, KybStatus> kyb = await _db.BusinessProfiles.AsNoTracking()
                .Where(p => p.UserId == u.Id)
                .ToDictionaryAsync(p => p.UserId, p => p.Status, ct);
            return TaoResponse(u, kyb);
        }

        private static AdminUserResponse TaoResponse(User u, Dictionary<Guid, KybStatus> kyb)
        {
            KybStatus s;
            string? kybStatus = null;
            if (kyb.TryGetValue(u.Id, out s))
            {
                kybStatus = KybService.TenTrangThai(s);
            }

            return new AdminUserResponse
            {
                UserId = u.Id,
                Email = u.Email,
                DisplayName = u.DisplayName,
                Roles = u.Roles,
                Status = u.Status == UserStatus.Blocked ? "blocked" : "active",
                BlockedAt = u.BlockedAt,
                BlockedReason = u.BlockedReason,
                KybStatus = kybStatus,
                CreatedAt = u.CreatedAt,
            };
        }
    }
}
