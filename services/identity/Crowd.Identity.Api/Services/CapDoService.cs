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
using Crowd.Identity.Api.Helpers;
using Crowd.Identity.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Crowd.Identity.Api.Services
{
    /// <summary>
    /// Cap do, huy hieu, bang xep hang cua labeler (FL-12).
    ///
    /// Nguon so lieu (kien truc 3.24):
    ///   - so cau DUOC CHAP NHAN (bai dat + khieu nai duoc chap nhan): hoi project-svc qua gRPC LabelerStatsData —
    ///     identity khong giu bo dem;
    ///   - uy tin moi nhat: reputation.changed (quality-svc).
    /// Khi nao tinh lai: project.grading_applied (project-svc da ghi ket qua cham), attempt.appeal_resolved
    /// chap nhan, reputation.changed → xep labeler vao hang cho level_dirty; CapDoWorker tinh theo lo. Cap doi →
    /// phat level.changed; huy hieu moi → badge.awarded.
    /// Goi tu consumer: KHONG SaveChanges (IdempotencyGuard luu mot lan).
    /// </summary>
    public sealed class CapDoService
    {
        public const int XepHangToiDa = 100;

        /// <summary>So labeler moi lo tinh lai cap.</summary>
        public const int LoTinhCap = 200;

        /// <summary>Ngay thong ke tinh theo gio Viet Nam (cung quy uoc voi thong ke thu nhap o ledger).</summary>
        private static readonly TimeSpan LechVietNam = TimeSpan.FromHours(7);

        private readonly IdentityDbContext _db;
        private readonly IOutboxWriter _outbox;
        private readonly ISettings _settings;
        private readonly HoSoService _hoSo;
        private readonly SoCauDuyetClient _soCau;
        private readonly IMemoryCache _cache;
        private readonly TimeProvider _clock;
        private readonly ILogger<CapDoService> _logger;

        public CapDoService(
            IdentityDbContext db,
            IOutboxWriter outbox,
            ISettings settings,
            HoSoService hoSo,
            SoCauDuyetClient soCau,
            IMemoryCache cache,
            TimeProvider clock,
            ILogger<CapDoService> logger)
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

            if (soCau == null)
            {
                throw new ArgumentNullException(nameof(soCau));
            }

            if (cache == null)
            {
                throw new ArgumentNullException(nameof(cache));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _db = db;
            _outbox = outbox;
            _settings = settings;
            _hoSo = hoSo;
            _soCau = soCau;
            _cache = cache;
            _clock = clock;
            _logger = logger;
        }

        // =====================================================================
        // GHI NHAN (tu consumer)
        // =====================================================================

        /// <summary>
        /// Xep labeler vao hang cho tinh lai cap. Chay TRONG transaction cua consumer; UPSERT nen hai event cung
        /// luc cho cung mot nguoi khong dung khoa chinh. Thu tu id co dinh — tranh deadlock giua hai consumer.
        /// </summary>
        public async Task XepTinhLaiAsync(IEnumerable<Guid> userIds, Guid correlationId, CancellationToken ct)
        {
            if (userIds == null)
            {
                throw new ArgumentNullException(nameof(userIds));
            }

            DateTimeOffset luc = _clock.GetUtcNow();
            foreach (Guid id in userIds.Distinct().OrderBy(x => x))
            {
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO level_dirty (user_id, dirty_at, correlation_id) VALUES ({id}, {luc}, {correlationId})
                    ON CONFLICT (user_id) DO UPDATE SET dirty_at = EXCLUDED.dirty_at, correlation_id = EXCLUDED.correlation_id", ct);
            }
        }

        public async Task GhiUyTinAsync(Guid userId, int uyTin, DateTimeOffset occurredAt, Guid correlationId, CancellationToken ct)
        {
            ThongKeLabeler tk = await LayHoacTaoAsync(userId, occurredAt, ct);
            if (tk.DatUyTin(uyTin, occurredAt))
            {
                await XepTinhLaiAsync(new[] { userId }, correlationId, ct);
            }
        }

        // =====================================================================
        // TINH LAI (CapDoWorker)
        // =====================================================================

        /// <summary>
        /// Lay mot lo trong hang cho (FOR UPDATE SKIP LOCKED — hai instance khong tinh trung), hoi so cau duoc
        /// chap nhan qua gRPC, tinh cap / huy hieu, xoa dong. project-svc khong tra loi → rollback, dong nam lai.
        /// Tra ve so labeler da tinh.
        /// </summary>
        public async Task<int> TinhLaiLoAsync(CancellationToken ct)
        {
            await using (var tx = await _db.Database.BeginTransactionAsync(ct))
            {
                List<HangChoTinhCap> lo = await _db.HangChoTinhCaps
                    .FromSqlInterpolated($@"
                        SELECT * FROM level_dirty ORDER BY dirty_at LIMIT {LoTinhCap} FOR UPDATE SKIP LOCKED")
                    .ToListAsync(ct);
                if (lo.Count == 0)
                {
                    return 0;
                }

                List<Guid> ids = lo.Select(x => x.UserId).ToList();
                Dictionary<Guid, long> soCau = await _soCau.DemAsync(ids, ct);

                DateTimeOffset luc = _clock.GetUtcNow();
                Dictionary<Guid, ThongKeLabeler> thongKe = await _db.ThongKeLabelers
                    .Where(t => ids.Contains(t.UserId))
                    .ToDictionaryAsync(t => t.UserId, ct);
                foreach (HangChoTinhCap dong in lo)
                {
                    ThongKeLabeler? tk;
                    if (!thongKe.TryGetValue(dong.UserId, out tk))
                    {
                        tk = ThongKeLabeler.Tao(dong.UserId, luc);
                        _db.ThongKeLabelers.Add(tk);
                    }

                    long n;
                    soCau.TryGetValue(dong.UserId, out n);
                    Caller caller = Caller.HeThong(dong.CorrelationId ?? Guid.NewGuid(), null);
                    await TinhLaiAsync(tk, (int)Math.Min(n, int.MaxValue), caller, luc, ct);
                }

                _db.HangChoTinhCaps.RemoveRange(lo);
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return lo.Count;
            }
        }

        // =====================================================================
        // DOC
        // =====================================================================

        public async Task<LevelInfoResponse> CuaToiAsync(Guid userId, CancellationToken ct)
        {
            ThongKeLabeler? tk = await _db.ThongKeLabelers.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
            List<HuyHieuDaDat> huyHieu = await _db.HuyHieuDaDats.AsNoTracking().Where(h => h.UserId == userId).ToListAsync(ct);
            long soCau = await _soCau.CuaMotNguoiAsync(userId, ct);

            int cap = tk == null ? 1 : tk.Level;
            DieuKienCap? ke = Luat().Where(d => d.Cap > cap).OrderBy(d => d.Cap).FirstOrDefault();
            return new LevelInfoResponse
            {
                Level = cap,
                ApprovedCount = (int)Math.Min(soCau, int.MaxValue),
                Reputation = tk == null ? null : tk.Reputation,
                NextLevel = ke == null ? null : new NextLevelDto { Level = ke.Cap, MinApproved = ke.SoNhanToiThieu, MinReputation = ke.UyTinToiThieu },
                Badges = HoSoService.TaoHuyHieu(huyHieu),
            };
        }

        /// <summary>
        /// Bang xep hang theo so cau duoc chap nhan. period: all | month (30 ngay) | week (7 ngay). Tai khoan bi
        /// khoa khong len bang. Ket qua giu trong bo nho dem (identity.stats_cache_ttl) — trang cong khai, doc
        /// nhieu; so lieu chi doi khi co du an cham xong.
        /// </summary>
        public async Task<IReadOnlyList<LeaderboardEntry>> BangXepHangAsync(string? period, int limit, DateTimeOffset bayGio, CancellationToken ct)
        {
            int soLuong = limit < 1 || limit > XepHangToiDa ? 20 : limit;
            string ky = string.IsNullOrWhiteSpace(period) ? "all" : period.Trim().ToLowerInvariant();

            DateTimeOffset? tu;
            if (ky == "all")
            {
                tu = null;
            }
            else if (ky == "month" || ky == "week")
            {
                tu = DauNgayVietNam(NgayVietNam(bayGio).AddDays(ky == "month" ? -29 : -6));
            }
            else
            {
                throw new InvalidValueException("ky_khong_hop_le", "period: all | month | week.");
            }

            string khoa = "xep_hang:" + ky + ":" + soLuong;
            IReadOnlyList<LeaderboardEntry>? co;
            if (_cache.TryGetValue(khoa, out co) && co != null)
            {
                return co;
            }

            List<DongSoCau> top = await TopConHoatDongAsync(tu, soLuong, ct);

            List<Guid> ids = top.Select(x => x.UserId).ToList();
            Dictionary<Guid, PublicProfileResponse> hoSo = (await _hoSo.CongKhaiAsync(ids, ct)).ToDictionary(p => p.UserId);

            List<LeaderboardEntry> kq = new List<LeaderboardEntry>();
            int hang = 0;
            foreach (DongSoCau dong in top)
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
                    ApprovedCount = (int)Math.Min(dong.SoCau, int.MaxValue),
                });
            }

            TimeSpan giu = _soCau.ThoiGianDem();
            if (giu > TimeSpan.Zero)
            {
                _cache.Set(khoa, (IReadOnlyList<LeaderboardEntry>)kq, giu);
            }

            return kq;
        }

        // =====================================================================
        // Ham dung chung
        // =====================================================================

        /// <summary>
        /// project-svc xep theo so cau, KHONG biet ai bi khoa — loc o day, thieu thi lay trang tiep (tai khoan bi
        /// khoa rat it nen thuong mot trang la du).
        /// </summary>
        private async Task<List<DongSoCau>> TopConHoatDongAsync(DateTimeOffset? tu, int soLuong, CancellationToken ct)
        {
            List<DongSoCau> top = new List<DongSoCau>();
            int offset = 0;
            int trang = soLuong * 2;
            for (int vong = 0; vong < 5 && top.Count < soLuong; vong++)
            {
                IReadOnlyList<DongSoCau> phan = await _soCau.XepHangAsync(tu, trang, offset, ct);
                if (phan.Count == 0)
                {
                    break;
                }

                List<Guid> ids = phan.Select(x => x.UserId).ToList();
                HashSet<Guid> hoatDong = new HashSet<Guid>(await _db.Users.AsNoTracking()
                    .Where(u => ids.Contains(u.Id) && u.Status == UserStatus.Active)
                    .Select(u => u.Id)
                    .ToListAsync(ct));
                top.AddRange(phan.Where(x => hoatDong.Contains(x.UserId)));
                offset = offset + phan.Count;
                if (phan.Count < trang)
                {
                    break;
                }
            }

            return top.Take(soLuong).ToList();
        }

        private async Task TinhLaiAsync(ThongKeLabeler tk, int soCau, Caller caller, DateTimeOffset luc, CancellationToken ct)
        {
            if (tk.DatCap(CapDo.TinhCap(Luat(), soCau, tk.Reputation), luc))
            {
                Phat(caller, new LevelChanged { UserId = tk.UserId, Level = tk.Level });
            }

            HashSet<string> daCo = new HashSet<string>(
                await _db.HuyHieuDaDats.Where(h => h.UserId == tk.UserId).Select(h => h.Badge).ToListAsync(ct),
                StringComparer.Ordinal);
            foreach (HuyHieu moi in CapDo.HuyHieuMoi(soCau, tk.Reputation, daCo))
            {
                _db.HuyHieuDaDats.Add(HuyHieuDaDat.Tao(tk.UserId, moi.Ma, luc));
                Phat(caller, new BadgeAwarded { UserId = tk.UserId, Badge = moi.Ma, Name = moi.Ten });
            }
        }

        private static DateOnly NgayVietNam(DateTimeOffset luc)
        {
            return DateOnly.FromDateTime(luc.ToOffset(LechVietNam).DateTime);
        }

        private static DateTimeOffset DauNgayVietNam(DateOnly ngay)
        {
            return new DateTimeOffset(ngay.ToDateTime(TimeOnly.MinValue), LechVietNam).ToUniversalTime();
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
