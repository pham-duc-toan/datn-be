using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Persistence;
using Crowd.BuildingBlocks.Security;
using Crowd.BuildingBlocks.Settings;
using Crowd.Link.Api.Dtos;
using Crowd.Link.Api.Entities;
using Crowd.Link.Api.Exceptions;
using Crowd.Link.Api.Helpers;
using Crowd.Link.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Crowd.Link.Api.Services
{
    /// <summary>Muc "Links" — dia chi cong khai cua trang vuot link.</summary>
    public sealed class ShortLinkOptions
    {
        public const string SectionName = "Links";

        /// <summary>Tien to link rut gon, vd "http://localhost:8080/g/".</summary>
        public string ShortBaseUrl { get; set; } = "http://localhost:8080/g/";
    }

    /// <summary>
    /// Link rut gon cua sharer: tao don le / hang loat / Quick Link / API (FS-01→03, 05),
    /// tuy chon (FS-06), chien dich, API key. Link moi o trang thai PendingScan — worker
    /// quet xong moi phat link.activated cho gate-svc.
    /// </summary>
    public sealed class LinkService
    {
        private const int SoLanThuSinhMa = 5;

        private readonly LinkDbContext _db;
        private readonly LinkEventPublisher _events;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;
        private readonly ShortLinkOptions _options;

        public LinkService(LinkDbContext db, LinkEventPublisher events, ISettings settings, TimeProvider clock, IOptions<ShortLinkOptions> options)
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

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            _db = db;
            _events = events;
            _settings = settings;
            _clock = clock;
            _options = options.Value;
        }

        // =====================================================================
        // TAO LINK
        // =====================================================================

        public async Task<LinkResponse> TaoAsync(CreateLinkRequest body, Guid ownerId, string? ipHash, CancellationToken ct)
        {
            if (body == null)
            {
                throw new LinkException(400, "thieu_du_lieu", "Thieu du lieu.");
            }

            ShortLink l = await DungLinkAsync(body.Url, body.Alias, body.Password, body.ExpiresAt, body.CampaignId, ownerId, ipHash, ct);
            await LuuLinkMoiAsync(l, string.IsNullOrWhiteSpace(body.Alias), ct);
            return TaoResponse(l);
        }

        /// <summary>FS-03: moi URL mot dong ket qua; dong hong khong lam hong ca lo.</summary>
        public async Task<IReadOnlyList<BulkLinkResult>> TaoHangLoatAsync(BulkCreateLinksRequest body, Guid ownerId, string? ipHash, CancellationToken ct)
        {
            if (body == null || body.Urls == null || body.Urls.Count == 0)
            {
                throw new LinkException(400, "thieu_url", "Can it nhat mot URL trong \"urls\".");
            }

            int toiDa = _settings.SoNguyen(SettingKeys.LinkBulkMax);
            if (body.Urls.Count > toiDa)
            {
                throw new LinkException(400, "qua_nhieu_url", "Toi da " + toiDa + " URL moi lan.");
            }

            List<BulkLinkResult> kq = new List<BulkLinkResult>();
            foreach (string url in body.Urls)
            {
                try
                {
                    ShortLink l = await DungLinkAsync(url, null, null, null, body.CampaignId, ownerId, ipHash, ct);
                    await LuuLinkMoiAsync(l, true, ct);
                    kq.Add(new BulkLinkResult { Url = url ?? string.Empty, Link = TaoResponse(l) });
                }
                catch (LinkException ex)
                {
                    _db.ChangeTracker.Clear();
                    kq.Add(new BulkLinkResult { Url = url ?? string.Empty, ErrorCode = ex.Code, Error = ex.Message });
                }
            }

            return kq;
        }

        private async Task<ShortLink> DungLinkAsync(
            string? url, string? alias, string? matKhau, DateTimeOffset? hetHan, Guid? campaignId, Guid ownerId, string? ipHash, CancellationToken ct)
        {
            UrlChuan u = UrlRules.ChuanHoa(url);
            string chuan = u.Url;
            string tenMien = u.TenMien;

            List<string> cha = UrlRules.CacTenMienCha(tenMien);
            BlockedDomain? chan = await _db.BlockedDomains.AsNoTracking().FirstOrDefaultAsync(d => cha.Contains(d.Domain), ct);
            if (chan != null)
            {
                throw new LinkException(400, "ten_mien_bi_chan", "Ten mien " + chan.Domain + " bi chan: " + chan.Reason);
            }

            if (campaignId.HasValue)
            {
                Guid c = campaignId.Value;
                bool cuaToi = await _db.Campaigns.AnyAsync(x => x.Id == c && x.OwnerId == ownerId, ct);
                if (!cuaToi)
                {
                    throw new LinkException(404, "khong_tim_thay_chien_dich", "Khong tim thay chien dich.");
                }
            }

            string ma;
            if (!string.IsNullOrWhiteSpace(alias))
            {
                ma = alias.Trim();
                UrlRules.KiemAlias(ma);
            }
            else
            {
                ma = UrlRules.SinhMa(_settings.SoNguyen(SettingKeys.LinkCodeLength));
            }

            return ShortLink.Tao(ownerId, ma, chuan, tenMien, BamMatKhau(matKhau), hetHan, campaignId, ipHash, _clock.GetUtcNow());
        }

        /// <summary>Ma ngau nhien trung (rat hiem) thi sinh lai; alias trung thi bao 409.</summary>
        private async Task LuuLinkMoiAsync(ShortLink l, bool maNgauNhien, CancellationToken ct)
        {
            for (int lan = 0; ; lan++)
            {
                _db.Links.Add(l);
                try
                {
                    await _db.SaveChangesAsync(ct);
                    return;
                }
                catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex, "ux_links_code"))
                {
                    _db.ChangeTracker.Clear();
                    if (!maNgauNhien)
                    {
                        throw new LinkException(409, "alias_da_ton_tai", "Alias '" + l.Code + "' da co nguoi dung.");
                    }

                    if (lan >= SoLanThuSinhMa)
                    {
                        throw;
                    }

                    l = ShortLink.Tao(
                        l.OwnerId, UrlRules.SinhMa(_settings.SoNguyen(SettingKeys.LinkCodeLength)), l.DestinationUrl, l.Domain,
                        l.PasswordHash, l.ExpiresAt, l.CampaignId, l.CreatorIpHash, _clock.GetUtcNow());
                }
            }
        }

        private static string? BamMatKhau(string? matKhau)
        {
            if (string.IsNullOrEmpty(matKhau))
            {
                return null;
            }

            if (matKhau.Length < 4 || matKhau.Length > 100)
            {
                throw new LinkException(400, "mat_khau_khong_hop_le", "Mat khau link phai tu 4 den 100 ky tu.");
            }

            return MatKhauLink.Bam(matKhau);
        }

        // =====================================================================
        // DOC / SUA / XOA
        // =====================================================================

        public async Task<PagedResponse<LinkResponse>> DanhSachAsync(
            Guid ownerId, Guid? campaignId, LinkStatus? status, int page, int pageSize, CancellationToken ct)
        {
            if (page < 1)
            {
                page = 1;
            }

            if (pageSize < 1 || pageSize > 100)
            {
                pageSize = 20;
            }

            IQueryable<ShortLink> q = _db.Links.AsNoTracking().Where(l => l.OwnerId == ownerId);
            if (campaignId.HasValue)
            {
                Guid c = campaignId.Value;
                q = q.Where(l => l.CampaignId == c);
            }

            if (status.HasValue)
            {
                LinkStatus s = status.Value;
                q = q.Where(l => l.Status == s);
            }

            int tong = await q.CountAsync(ct);
            List<ShortLink> ds = await q.OrderByDescending(l => l.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            return new PagedResponse<LinkResponse> { Items = ds.Select(TaoResponse).ToList(), Page = page, PageSize = pageSize, Total = tong };
        }

        public async Task<LinkResponse> XemAsync(Guid id, Guid ownerId, CancellationToken ct)
        {
            return TaoResponse(await LayCuaToiAsync(id, ownerId, ct));
        }

        /// <summary>Doi tuy chon (mat khau / het han / chien dich). Link dang chay thi phat lai link.activated.</summary>
        public async Task<LinkResponse> CapNhatAsync(Guid id, UpdateLinkRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new LinkException(400, "thieu_du_lieu", "Thieu du lieu.");
            }

            ShortLink l = await LayCuaToiAsync(id, caller.LayUserId(), ct);

            if (body.CampaignId.HasValue)
            {
                Guid c = body.CampaignId.Value;
                Guid owner = l.OwnerId;
                if (!await _db.Campaigns.AnyAsync(x => x.Id == c && x.OwnerId == owner, ct))
                {
                    throw new LinkException(404, "khong_tim_thay_chien_dich", "Khong tim thay chien dich.");
                }
            }

            string? bam = body.ChangePassword ? BamMatKhau(body.Password) : l.PasswordHash;
            l.CapNhatTuyChon(bam, body.ChangePassword, body.ExpiresAt, body.CampaignId, _clock.GetUtcNow());

            if (l.Status == LinkStatus.Active)
            {
                _events.PhatKichHoat(l, caller);
            }

            await _db.SaveChangesAsync(ct);
            return TaoResponse(l);
        }

        /// <summary>Chu link xoa: ngung phuc vu, KHONG giu doanh thu (khong phai vi pham).</summary>
        public async Task XoaAsync(Guid id, Caller caller, CancellationToken ct)
        {
            ShortLink l = await LayCuaToiAsync(id, caller.LayUserId(), ct);
            bool dangPhucVu = l.Status == LinkStatus.Active;
            if (l.VoHieuHoa("Chu link xoa", _clock.GetUtcNow()) && dangPhucVu)
            {
                _events.PhatVoHieu(l, false, caller);
            }

            await _db.SaveChangesAsync(ct);
        }

        private async Task<ShortLink> LayCuaToiAsync(Guid id, Guid ownerId, CancellationToken ct)
        {
            ShortLink? l = await _db.Links.FirstOrDefaultAsync(x => x.Id == id, ct);

            // Link cua nguoi khac → 404 (BOLA).
            if (l == null || l.OwnerId != ownerId)
            {
                throw new LinkException(404, "khong_tim_thay", "Khong tim thay link.");
            }

            return l;
        }

        // =====================================================================
        // CHIEN DICH
        // =====================================================================

        public async Task<CampaignResponse> TaoChienDichAsync(CreateCampaignRequest body, Guid ownerId, CancellationToken ct)
        {
            string ten = body == null || body.Name == null ? string.Empty : body.Name.Trim();
            if (ten.Length == 0 || ten.Length > Campaign.CotTen)
            {
                throw new LinkException(400, "ten_chien_dich_khong_hop_le", "Ten chien dich 1-" + Campaign.CotTen + " ky tu.");
            }

            Campaign c = Campaign.Tao(ownerId, ten, _clock.GetUtcNow());
            _db.Campaigns.Add(c);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex, "ux_campaigns_owner_name"))
            {
                throw new LinkException(409, "chien_dich_da_ton_tai", "Ban da co chien dich ten nay.");
            }

            return new CampaignResponse { Id = c.Id, Name = c.Name, LinkCount = 0, CreatedAt = c.CreatedAt };
        }

        public async Task<IReadOnlyList<CampaignResponse>> DanhSachChienDichAsync(Guid ownerId, CancellationToken ct)
        {
            List<Campaign> ds = await _db.Campaigns.AsNoTracking().Where(c => c.OwnerId == ownerId).OrderBy(c => c.Name).ToListAsync(ct);
            Dictionary<Guid, int> dem = await _db.Links.AsNoTracking()
                .Where(l => l.OwnerId == ownerId && l.CampaignId != null)
                .GroupBy(l => l.CampaignId!.Value)
                .ToDictionaryAsync(g => g.Key, g => g.Count(), ct);

            List<CampaignResponse> kq = new List<CampaignResponse>();
            foreach (Campaign c in ds)
            {
                int n;
                kq.Add(new CampaignResponse { Id = c.Id, Name = c.Name, LinkCount = dem.TryGetValue(c.Id, out n) ? n : 0, CreatedAt = c.CreatedAt });
            }

            return kq;
        }

        // =====================================================================
        // API KEY (FS-05, FS-02)
        // =====================================================================

        public async Task<ApiKeyResponse> TaoApiKeyAsync(Guid userId, CancellationToken ct)
        {
            string key = "lk_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
            string prefix = key.Substring(0, 9);
            DateTimeOffset bayGio = _clock.GetUtcNow();

            ApiKey? cu = await _db.ApiKeys.FirstOrDefaultAsync(k => k.UserId == userId, ct);
            if (cu == null)
            {
                _db.ApiKeys.Add(ApiKey.Tao(userId, BamKey(key), prefix, bayGio));
            }
            else
            {
                cu.DoiKey(BamKey(key), prefix, bayGio);
            }

            await _db.SaveChangesAsync(ct);
            return new ApiKeyResponse { ApiKey = key, Prefix = prefix, CreatedAt = bayGio };
        }

        public async Task<ApiKeyResponse?> XemApiKeyAsync(Guid userId, CancellationToken ct)
        {
            ApiKey? k = await _db.ApiKeys.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
            return k == null ? null : new ApiKeyResponse { Prefix = k.Prefix, CreatedAt = k.CreatedAt };
        }

        /// <summary>API key → chu key. null = key sai / da bi thay.</summary>
        public async Task<Guid?> XacThucApiKeyAsync(string? key, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > 100)
            {
                return null;
            }

            string bam = BamKey(key.Trim());
            ApiKey? k = await _db.ApiKeys.AsNoTracking().FirstOrDefaultAsync(x => x.KeyHash == bam, ct);
            return k == null ? null : k.UserId;
        }

        private static string BamKey(string key)
        {
            // Key ngau nhien 192 bit: sha256 khong muoi la du (khong ai doan duoc key goc tu bam).
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        }

        // =====================================================================

        public LinkResponse TaoResponse(ShortLink l)
        {
            if (l == null)
            {
                throw new ArgumentNullException(nameof(l));
            }

            return new LinkResponse
            {
                Id = l.Id,
                Code = l.Code,
                ShortUrl = _options.ShortBaseUrl + l.Code,
                DestinationUrl = l.DestinationUrl,
                Domain = l.Domain,
                Status = l.Status,
                StatusReason = l.StatusReason,
                HasPassword = l.PasswordHash != null,
                ExpiresAt = l.ExpiresAt,
                CampaignId = l.CampaignId,
                CreatedAt = l.CreatedAt,
            };
        }
    }
}
