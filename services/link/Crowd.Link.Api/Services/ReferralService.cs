using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Persistence;
using Crowd.BuildingBlocks.Settings;
using Crowd.Contracts.Link;
using Crowd.Link.Api.Dtos;
using Crowd.Link.Api.Entities;
using Crowd.Link.Api.Exceptions;
using Crowd.Link.Api.Helpers;
using Crowd.Link.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Link.Api.Services
{
    /// <summary>
    /// Gioi thieu (FS-08). link-svc giu QUAN HE gioi thieu; tien hoa hong do ledger tinh
    /// (nghe referral.registered) va chi tra khi nguoi duoc moi da tu kiem vuot nguong
    /// (VD-L-05 — chong tai khoan clone tu moi nhau).
    ///
    /// Chi tai khoan MOI (trong cua so link.referral_claim_window ke tu luc dang ky) moi
    /// nhap duoc ma: khong cho nguoi cu "gan" minh vao ai do sau khi da kiem tien.
    /// </summary>
    public sealed class ReferralService
    {
        private const int DoDaiMa = 8;

        private readonly LinkDbContext _db;
        private readonly LinkEventPublisher _events;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;

        public ReferralService(LinkDbContext db, LinkEventPublisher events, ISettings settings, TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
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
            _events = events;
            _settings = settings;
            _clock = clock;
        }

        /// <summary>Ma gioi thieu cua toi (tao lan dau) + so nguoi da moi.</summary>
        public async Task<ReferralInfoResponse> CuaToiAsync(Guid userId, CancellationToken ct)
        {
            ReferralCode? ma = await _db.ReferralCodes.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == userId, ct);
            if (ma == null)
            {
                ma = ReferralCode.Tao(userId, UrlRules.SinhMa(DoDaiMa), _clock.GetUtcNow());
                _db.ReferralCodes.Add(ma);
                try
                {
                    await _db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex))
                {
                    // Hai request cung luc (hoac trung ma rat hiem): doc lai ban da luu.
                    _db.ChangeTracker.Clear();
                    ma = await _db.ReferralCodes.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == userId, ct);
                    if (ma == null)
                    {
                        throw new LinkException(409, "xung_dot_dong_thoi", "Thu lai sau it giay.");
                    }
                }
            }

            int soNguoi = await _db.Referrals.CountAsync(r => r.ReferrerId == userId, ct);
            Referral? cuaToi = await _db.Referrals.AsNoTracking().FirstOrDefaultAsync(r => r.ReferredId == userId, ct);
            return new ReferralInfoResponse { Code = ma.Code, ReferredCount = soNguoi, ReferredBy = cuaToi == null ? null : cuaToi.ReferrerId };
        }

        public async Task NhapMaAsync(ClaimReferralRequest body, Caller caller, string? ipHash, CancellationToken ct)
        {
            string ma = body == null || body.Code == null ? string.Empty : body.Code.Trim();
            Guid toi = caller.LayUserId();

            ReferralCode? chu = await _db.ReferralCodes.AsNoTracking().FirstOrDefaultAsync(r => r.Code == ma, ct);
            if (chu == null)
            {
                throw new LinkException(404, "ma_gioi_thieu_sai", "Ma gioi thieu khong ton tai.");
            }

            if (chu.UserId == toi)
            {
                throw new LinkException(409, "tu_gioi_thieu", "Khong nhap ma gioi thieu cua chinh minh.");
            }

            KnownUser? u = await _db.KnownUsers.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == toi, ct);
            DateTimeOffset bayGio = _clock.GetUtcNow();
            if (u == null || bayGio - u.RegisteredAt > _settings.ThoiGian(SettingKeys.LinkReferralClaimWindow))
            {
                throw new LinkException(409, "qua_han_nhap_ma", "Chi tai khoan moi dang ky moi nhap duoc ma gioi thieu.");
            }

            // Vong A moi B, B moi A: chan.
            bool vong = await _db.Referrals.AnyAsync(r => r.ReferrerId == toi && r.ReferredId == chu.UserId, ct);
            if (vong)
            {
                throw new LinkException(409, "gioi_thieu_vong", "Hai tai khoan khong the gioi thieu lan nhau.");
            }

            // Chong lam dung (VD-L-05 mo rong): dau hieu nghi ngo → ghi nhan nhung CHUA phat referral.registered
            // (ledger chua tinh hoa hong) cho admin xet. Khong chan nguoi dung: NAT / wifi chung de bao nham.
            string? dauHieu = await DanhGiaAsync(chu.UserId, ipHash, bayGio, ct);
            _db.Referrals.Add(Referral.Tao(chu.UserId, toi, bayGio, ipHash, dauHieu));
            if (dauHieu == null)
            {
                _events.Phat(caller, new ReferralRegistered { ReferrerId = chu.UserId, ReferredId = toi, RegisteredAt = bayGio });
            }
            else
            {
                _events.Phat(caller, new ReferralFlagged { ReferrerId = chu.UserId, ReferredId = toi, Reasons = dauHieu });
            }

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex, "PK_referrals"))
            {
                throw new LinkException(409, "da_co_nguoi_gioi_thieu", "Ban da nhap ma gioi thieu roi.");
            }
        }

        /// <summary>Dem so lieu roi goi luat thuan DauHieuLamDung.</summary>
        private async Task<string?> DanhGiaAsync(Guid nguoiGioiThieu, string? ipHash, DateTimeOffset bayGio, CancellationToken ct)
        {
            bool trungIpNguoiGioiThieu = false;
            int cungIp24h = 0;
            if (!string.IsNullOrEmpty(ipHash))
            {
                trungIpNguoiGioiThieu = await _db.Links.AnyAsync(l => l.OwnerId == nguoiGioiThieu && l.CreatorIpHash == ipHash, ct);
                DateTimeOffset tu24h = bayGio - TimeSpan.FromHours(24);
                cungIp24h = await _db.Referrals.CountAsync(
                    r => r.ReferrerId == nguoiGioiThieu && r.ClaimIpHash == ipHash && r.CreatedAt > tu24h, ct);
            }

            DateTimeOffset tu1h = bayGio - TimeSpan.FromHours(1);
            int trongGio = await _db.Referrals.CountAsync(r => r.ReferrerId == nguoiGioiThieu && r.CreatedAt > tu1h, ct);
            return DauHieuLamDung(trungIpNguoiGioiThieu, cungIp24h, trongGio,
                _settings.SoNguyen(SettingKeys.LinkReferralMaxPerIp), _settings.SoNguyen(SettingKeys.LinkReferralBurstPerHour));
        }

        /// <summary>
        /// Luat thuan (test duoc). So lieu la cac gioi thieu DA CO (chua gom lan nay):
        ///   trung_ip_nguoi_gioi_thieu  nguoi duoc moi nhap ma tu IP nguoi gioi thieu tung tao link (tu moi chinh minh)
        ///   nhieu_tai_khoan_cung_ip    trong 24h nguoi gioi thieu da co &gt;= toiDaMoiIp nguoi nhap ma cung IP nay
        ///   dot_bien                   trong 1 gio nguoi gioi thieu da co &gt;= toiDaMoiGio nguoi nhap ma
        /// null = binh thuong.
        /// </summary>
        public static string? DauHieuLamDung(bool trungIpNguoiGioiThieu, int cungIp24h, int trongGio, int toiDaMoiIp, int toiDaMoiGio)
        {
            System.Collections.Generic.List<string> ds = new System.Collections.Generic.List<string>();
            if (trungIpNguoiGioiThieu)
            {
                ds.Add("trung_ip_nguoi_gioi_thieu");
            }

            if (cungIp24h >= toiDaMoiIp)
            {
                ds.Add("nhieu_tai_khoan_cung_ip");
            }

            if (trongGio >= toiDaMoiGio)
            {
                ds.Add("dot_bien");
            }

            return ds.Count == 0 ? null : string.Join(",", ds);
        }

        // =====================================================================
        // ADMIN: xet gioi thieu nghi ngo
        // =====================================================================

        public async Task<System.Collections.Generic.IReadOnlyList<FlaggedReferralResponse>> NghiNgoAsync(CancellationToken ct)
        {
            System.Collections.Generic.List<Referral> ds = await _db.Referrals.AsNoTracking()
                .Where(r => r.Status == TrangThaiGioiThieu.Flagged)
                .OrderByDescending(r => r.CreatedAt).Take(200).ToListAsync(ct);
            return ds.Select(r => new FlaggedReferralResponse
            {
                ReferrerId = r.ReferrerId,
                ReferredId = r.ReferredId,
                Reasons = r.FlagReasons ?? string.Empty,
                CreatedAt = r.CreatedAt,
            }).ToList();
        }

        /// <summary>Chap nhan → phat referral.registered (luc nhap ma goc) — ledger bat dau tinh hoa hong. Bac → referral.rejected.</summary>
        public async Task XetAsync(Guid referredId, bool chapNhan, string? lyDo, Caller caller, CancellationToken ct)
        {
            Referral? r = await _db.Referrals.FirstOrDefaultAsync(x => x.ReferredId == referredId, ct);
            if (r == null)
            {
                throw new LinkException(404, "khong_tim_thay", "Khong tim thay gioi thieu.");
            }

            if (!r.Xet(chapNhan, caller.LayUserId(), _clock.GetUtcNow()))
            {
                throw new LinkException(409, "da_xet", "Gioi thieu nay khong o trang thai cho xet.");
            }

            if (chapNhan)
            {
                _events.Phat(caller, new ReferralRegistered { ReferrerId = r.ReferrerId, ReferredId = r.ReferredId, RegisteredAt = r.CreatedAt });
            }
            else
            {
                _events.Phat(caller, new ReferralRejected
                {
                    ReferrerId = r.ReferrerId,
                    ReferredId = r.ReferredId,
                    Reason = string.IsNullOrWhiteSpace(lyDo) ? "lam_dung_gioi_thieu" : lyDo.Trim(),
                });
            }

            await _db.SaveChangesAsync(ct);
        }
    }
}
