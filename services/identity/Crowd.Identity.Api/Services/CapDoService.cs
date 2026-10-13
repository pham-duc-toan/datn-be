using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.BuildingBlocks.Settings;
using Crowd.Contracts.Identity;
using Crowd.Identity.Api.Dtos;
using Crowd.Identity.Api.Entities;
using Crowd.Identity.Api.Exceptions;
using Crowd.Identity.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crowd.Identity.Api.Services
{
    /// <summary>
    /// Cap do, huy hieu, bang xep hang cua labeler (FL-12).
    ///
    /// Nguon so lieu (qua event, khong goi service khac):
    ///   - annotation.approved (kenh chuyen nghiep) → +1 nhan duoc duyet;
    ///   - dispute.resolved ket cuc tra doanh nghiep → -1 (nhan bi loai);
    ///   - reputation.changed (quality-svc) → uy tin moi nhat.
    /// Cap doi → phat level.changed (task-svc loc dieu kien minLevel). Huy hieu moi → badge.awarded.
    /// Goi tu consumer: KHONG SaveChanges (IdempotencyGuard luu mot lan).
    /// </summary>
    public sealed class CapDoService
    {
        public const int XepHangToiDa = 100;

        /// <summary>Ngay thong ke tinh theo gio Viet Nam (cung quy uoc voi thong ke thu nhap o ledger).</summary>
        private static readonly TimeSpan LechVietNam = TimeSpan.FromHours(7);

        private readonly IdentityDbContext _db;
        private readonly IOutboxWriter _outbox;
        private readonly ISettings _settings;
        private readonly HoSoService _hoSo;
        private readonly ILogger<CapDoService> _logger;

        public CapDoService(IdentityDbContext db, IOutboxWriter outbox, ISettings settings, HoSoService hoSo, ILogger<CapDoService> logger)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (outbox == null)
            {
                throw new ArgumentNullException(nameof(outbox));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (hoSo == null)
            {
                throw new ArgumentNullException(nameof(hoSo));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _db = db;
            _outbox = outbox;
            _settings = settings;
            _hoSo = hoSo;
            _logger = logger;
        }

        // =====================================================================
        // GHI NHAN (tu consumer)
        // =====================================================================

        /// <summary>Cong / tru so nhan duoc duyet (soNhan = 1 hoac -1) roi tinh lai cap + huy hieu.</summary>
        public async Task GhiNhanDuyetAsync(Guid labelerId, int soNhan, DateTimeOffset luc, Caller caller, CancellationToken ct)
        {
            ThongKeLabeler tk = await LayHoacTaoAsync(labelerId, luc, ct);
            tk.CongNhan(soNhan, luc);

            DateOnly ngay = NgayVietNam(luc);
            ThongKeNgay? theoNgay = await _db.ThongKeNgays.FirstOrDefaultAsync(x => x.UserId == labelerId && x.Ngay == ngay, ct);
            if (theoNgay == null)
            {
                theoNgay = ThongKeNgay.Tao(labelerId, ngay);
                _db.ThongKeNgays.Add(theoNgay);
            }

            theoNgay.Cong(soNhan);
            await TinhLaiAsync(tk, caller, luc, ct);
        }

        public async Task GhiUyTinAsync(Guid userId, int uyTin, DateTimeOffset occurredAt, Caller caller, CancellationToken ct)
        {
            ThongKeLabeler tk = await LayHoacTaoAsync(userId, occurredAt, ct);
            if (tk.DatUyTin(uyTin, occurredAt))
            {
                await TinhLaiAsync(tk, caller, occurredAt, ct);
            }
        }

        // =====================================================================
        // DOC
        // =====================================================================

        public async Task<LevelInfoResponse> CuaToiAsync(Guid userId, CancellationToken ct)
        {
            ThongKeLabeler? tk = await _db.ThongKeLabelers.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
            List<HuyHieuDaDat> huyHieu = await _db.HuyHieuDaDats.AsNoTracking().Where(h => h.UserId == userId).ToListAsync(ct);

            int cap = tk == null ? 1 : tk.Level;
            DieuKienCap? ke = Luat().Where(d => d.Cap > cap).OrderBy(d => d.Cap).FirstOrDefault();
            return new LevelInfoResponse
            {
                Level = cap,
                ApprovedCount = tk == null ? 0 : tk.ApprovedCount,
                Reputation = tk == null ? null : tk.Reputation,
                NextLevel = ke == null ? null : new NextLevelDto { Level = ke.Cap, MinApproved = ke.SoNhanToiThieu, MinReputation = ke.UyTinToiThieu },
                Badges = HoSoService.TaoHuyHieu(huyHieu),
            };
        }

        /// <summary>
        /// Bang xep hang theo so nhan duoc duyet. period: all | month (30 ngay) | week (7 ngay).
        /// Tai khoan bi khoa khong len bang.
        /// </summary>
        public async Task<IReadOnlyList<LeaderboardEntry>> BangXepHangAsync(string? period, int limit, DateTimeOffset bayGio, CancellationToken ct)
        {
            int soLuong = limit < 1 || limit > XepHangToiDa ? 20 : limit;
            string ky = string.IsNullOrWhiteSpace(period) ? "all" : period.Trim().ToLowerInvariant();

            List<DongXepHang> top;
            if (ky == "all")
            {
                top = await _db.ThongKeLabelers.AsNoTracking()
                    .Where(t => t.ApprovedCount > 0
                                && _db.Users.Any(u => u.Id == t.UserId && u.Status == UserStatus.Active))
                    .OrderByDescending(t => t.ApprovedCount).ThenBy(t => t.UserId)
                    .Take(soLuong)
                    .Select(t => new DongXepHang { UserId = t.UserId, SoNhan = t.ApprovedCount })
                    .ToListAsync(ct);
            }
            else if (ky == "month" || ky == "week")
            {
                DateOnly tu = NgayVietNam(bayGio).AddDays(ky == "month" ? -29 : -6);
                top = await _db.ThongKeNgays.AsNoTracking()
                    .Where(n => n.Ngay >= tu
                                && _db.Users.Any(u => u.Id == n.UserId && u.Status == UserStatus.Active))
                    .GroupBy(n => n.UserId)
                    .Select(g => new DongXepHang { UserId = g.Key, SoNhan = g.Sum(n => n.ApprovedCount) })
                    .Where(x => x.SoNhan > 0)
                    .OrderByDescending(x => x.SoNhan).ThenBy(x => x.UserId)
                    .Take(soLuong)
                    .ToListAsync(ct);
            }
            else
            {
                throw new InvalidValueException("ky_khong_hop_le", "period: all | month | week.");
            }

            List<Guid> ids = top.Select(x => x.UserId).ToList();
            Dictionary<Guid, PublicProfileResponse> hoSo = (await _hoSo.CongKhaiAsync(ids, ct)).ToDictionary(p => p.UserId);

            List<LeaderboardEntry> kq = new List<LeaderboardEntry>();
            int hang = 0;
            foreach (DongXepHang dong in top)
            {
                PublicProfileResponse? p;
                if (!hoSo.TryGetValue(dong.UserId, out p))
                {
                    continue;
                }

                hang = hang + 1;
                kq.Add(new LeaderboardEntry
                {
                    Rank = hang,
                    UserId = p.UserId,
                    DisplayName = p.DisplayName,
                    AvatarUrl = p.AvatarUrl,
                    Level = p.Level,
                    ApprovedCount = dong.SoNhan,
                });
            }

            return kq;
        }

        // =====================================================================
        // Ham dung chung
        // =====================================================================

        private async Task TinhLaiAsync(ThongKeLabeler tk, Caller caller, DateTimeOffset luc, CancellationToken ct)
        {
            if (tk.DatCap(CapDo.TinhCap(Luat(), tk.ApprovedCount, tk.Reputation)))
            {
                Phat(caller, new LevelChanged { UserId = tk.UserId, Level = tk.Level });
            }

            HashSet<string> daCo = new HashSet<string>(
                await _db.HuyHieuDaDats.Where(h => h.UserId == tk.UserId).Select(h => h.Badge).ToListAsync(ct),
                StringComparer.Ordinal);
            foreach (HuyHieu moi in CapDo.HuyHieuMoi(tk.ApprovedCount, tk.Reputation, daCo))
            {
                _db.HuyHieuDaDats.Add(HuyHieuDaDat.Tao(tk.UserId, moi.Ma, luc));
                Phat(caller, new BadgeAwarded { UserId = tk.UserId, Badge = moi.Ma, Name = moi.Ten });
            }
        }

        private static DateOnly NgayVietNam(DateTimeOffset luc)
        {
            return DateOnly.FromDateTime(luc.ToOffset(LechVietNam).DateTime);
        }

        private IReadOnlyList<DieuKienCap> Luat()
        {
            IReadOnlyList<DieuKienCap>? luat = CapDo.DocLuat(_settings.Chuoi(SettingKeys.IdentityLevelRules));
            if (luat == null)
            {
                _logger.LogWarning("Setting identity.level_rules sai dinh dang — dung luat mac dinh");
                luat = CapDo.DocLuat(CapDo.LuatMacDinh)!;
            }

            return luat;
        }

        private async Task<ThongKeLabeler> LayHoacTaoAsync(Guid userId, DateTimeOffset luc, CancellationToken ct)
        {
            ThongKeLabeler? tk = await _db.ThongKeLabelers.FirstOrDefaultAsync(x => x.UserId == userId, ct);
            if (tk == null)
            {
                tk = ThongKeLabeler.Tao(userId, luc);
                _db.ThongKeLabelers.Add(tk);
            }

            return tk;
        }

        private sealed class DongXepHang
        {
            public Guid UserId { get; init; }

            public int SoNhan { get; init; }
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
    }
}
