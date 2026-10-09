using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Persistence;
using Crowd.BuildingBlocks.Settings;
using Crowd.Link.Api.Dtos;
using Crowd.Link.Api.Entities;
using Crowd.Link.Api.Exceptions;
using Crowd.Link.Api.Helpers;
using Crowd.Link.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crowd.Link.Api.Services
{
    /// <summary>
    /// Kiem duyet link dich (VD-L-01): quet luc tao, bao cao tu trang vuot link,
    /// hang doi kiem duyet cho admin, ten mien bi chan. Vi pham da xac nhan thi vo
    /// hieu hoa VA giu doanh thu dang treo (ledger nghe link.disabled).
    /// </summary>
    public sealed class ModerationService
    {
        private readonly LinkDbContext _db;
        private readonly LinkEventPublisher _events;
        private readonly LinkService _links;
        private readonly IUrlSafetyChecker _quet;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;
        private readonly ILogger<ModerationService> _logger;

        public ModerationService(
            LinkDbContext db,
            LinkEventPublisher events,
            LinkService links,
            IUrlSafetyChecker quet,
            ISettings settings,
            TimeProvider clock,
            ILogger<ModerationService> logger)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            if (links == null)
            {
                throw new ArgumentNullException(nameof(links));
            }

            if (quet == null)
            {
                throw new ArgumentNullException(nameof(quet));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
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
            _events = events;
            _links = links;
            _quet = quet;
            _settings = settings;
            _clock = clock;
            _logger = logger;
        }

        // =====================================================================
        // QUET LINK MOI (worker)
        // =====================================================================

        /// <summary>
        /// Quet mot lo link PendingScan. Moi link mot transaction, khoa dong bang
        /// FOR UPDATE SKIP LOCKED — nhieu ban sao worker khong quet trung.
        /// </summary>
        public async Task<int> QuetMotLoAsync(int coLo, CancellationToken ct)
        {
            List<Guid> ids = await _db.Links.AsNoTracking()
                .Where(l => l.Status == LinkStatus.PendingScan)
                .OrderBy(l => l.CreatedAt)
                .Select(l => l.Id)
                .Take(coLo)
                .ToListAsync(ct);

            int dem = 0;
            foreach (Guid id in ids)
            {
                _db.ChangeTracker.Clear();
                var tx = await _db.Database.BeginTransactionAsync(ct);
                try
                {
                    ShortLink? l = await _db.Links
                        .FromSqlInterpolated($"SELECT *, xmin FROM links WHERE id = {id} AND status = 'PendingScan' FOR UPDATE SKIP LOCKED")
                        .FirstOrDefaultAsync(ct);
                    if (l == null)
                    {
                        await tx.RollbackAsync(ct);
                        continue;
                    }

                    DateTimeOffset bayGio = _clock.GetUtcNow();
                    Caller heThong = Caller.HeThong(Guid.CreateVersion7(), null);

                    List<string> cha = UrlRules.CacTenMienCha(l.Domain);
                    BlockedDomain? chan = await _db.BlockedDomains.AsNoTracking().FirstOrDefaultAsync(d => cha.Contains(d.Domain), ct);
                    string? lyDo = chan != null
                        ? "Ten mien " + chan.Domain + " bi chan: " + chan.Reason
                        : await _quet.KiemAsync(l.DestinationUrl, l.Domain, ct);

                    if (lyDo != null)
                    {
                        l.Chan(lyDo, bayGio);
                        _logger.LogWarning("Chan link {Code} → {Url}: {LyDo}", l.Code, l.DestinationUrl, lyDo);
                    }
                    else
                    {
                        l.KichHoat(bayGio);
                        _events.PhatKichHoat(l, heThong);
                    }

                    await _db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    dem++;
                }
                finally
                {
                    await tx.DisposeAsync();
                }
            }

            return dem;
        }

        // =====================================================================
        // BAO CAO (khach vang lai)
        // =====================================================================

        public async Task BaoCaoAsync(string code, string? lyDo, string ipHash, CancellationToken ct)
        {
            string ly = lyDo == null ? string.Empty : lyDo.Trim();
            if (ly.Length == 0 || ly.Length > LinkReport.CotLyDo)
            {
                throw new LinkException(400, "thieu_ly_do", "Ly do bao cao 1-" + LinkReport.CotLyDo + " ky tu.");
            }

            ShortLink? l = await _db.Links.FirstOrDefaultAsync(x => x.Code == code, ct);
            if (l == null)
            {
                throw new LinkException(404, "khong_tim_thay", "Khong tim thay link.");
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            _db.Reports.Add(LinkReport.Tao(l.Id, ipHash, ly, bayGio));
            bool vuaVuotNguong = l.GhiBaoCao(_settings.SoNguyen(SettingKeys.LinkReportReviewThreshold), bayGio);

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex, "ux_link_reports_link_ip"))
            {
                // Cung IP bao cao lan hai: khong dem them, tra ve nhu thanh cong (khong lo gi cho ke spam).
                return;
            }

            if (vuaVuotNguong)
            {
                _logger.LogWarning("Link {Code} bi bao cao {So} lan — vao hang doi kiem duyet", l.Code, l.ReportCount);
            }
        }

        // =====================================================================
        // ADMIN
        // =====================================================================

        public async Task<PagedResponse<ReviewLinkResponse>> HangDoiAsync(int page, int pageSize, CancellationToken ct)
        {
            if (page < 1)
            {
                page = 1;
            }

            if (pageSize < 1 || pageSize > 100)
            {
                pageSize = 20;
            }

            IQueryable<ShortLink> q = _db.Links.AsNoTracking().Where(l => l.NeedsReview);
            int tong = await q.CountAsync(ct);
            List<ShortLink> ds = await q.OrderByDescending(l => l.ReportCount).ThenBy(l => l.UpdatedAt)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

            List<Guid> ids = ds.Select(l => l.Id).ToList();
            List<LinkReport> baoCao = await _db.Reports.AsNoTracking()
                .Where(r => ids.Contains(r.LinkId))
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync(ct);

            List<ReviewLinkResponse> items = new List<ReviewLinkResponse>();
            foreach (ShortLink l in ds)
            {
                List<string> lyDo = baoCao.Where(r => r.LinkId == l.Id).Take(5).Select(r => r.Reason).ToList();
                items.Add(new ReviewLinkResponse { Link = _links.TaoResponse(l), OwnerId = l.OwnerId, ReportCount = l.ReportCount, RecentReasons = lyDo });
            }

            return new PagedResponse<ReviewLinkResponse> { Items = items, Page = page, PageSize = pageSize, Total = tong };
        }

        /// <summary>Vo hieu hoa vi vi pham. giuDoanhThu mac dinh true: ledger giu lai doanh thu dang treo cua link.</summary>
        public async Task<LinkResponse> VoHieuHoaAsync(Guid id, DisableLinkRequest? body, Caller caller, CancellationToken ct)
        {
            string ly = body == null || body.Reason == null ? string.Empty : body.Reason.Trim();
            if (ly.Length == 0)
            {
                throw new LinkException(400, "thieu_ly_do", "Vo hieu hoa link phai ghi ly do.");
            }

            bool giu = body == null || body.WithholdRevenue == null || body.WithholdRevenue.Value;

            ShortLink? l = await _db.Links.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (l == null)
            {
                throw new LinkException(404, "khong_tim_thay", "Khong tim thay link.");
            }

            if (!l.VoHieuHoa("Admin: " + ly, _clock.GetUtcNow()))
            {
                throw new LinkException(409, "link_da_ngung", "Link da ngung phuc vu tu truoc.");
            }

            _events.PhatVoHieu(l, giu, caller);
            await _db.SaveChangesAsync(ct);
            return _links.TaoResponse(l);
        }

        public async Task<LinkResponse> BoQuaBaoCaoAsync(Guid id, CancellationToken ct)
        {
            ShortLink? l = await _db.Links.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (l == null)
            {
                throw new LinkException(404, "khong_tim_thay", "Khong tim thay link.");
            }

            l.BoQuaBaoCao(_clock.GetUtcNow());
            await _db.SaveChangesAsync(ct);
            return _links.TaoResponse(l);
        }

        public async Task<IReadOnlyList<BlockedDomainResponse>> DanhSachTenMienChanAsync(CancellationToken ct)
        {
            List<BlockedDomain> ds = await _db.BlockedDomains.AsNoTracking().OrderBy(d => d.Domain).ToListAsync(ct);
            return ds.Select(d => new BlockedDomainResponse { Domain = d.Domain, Reason = d.Reason, CreatedAt = d.CreatedAt }).ToList();
        }

        /// <summary>
        /// Chan ten mien: link dang chay / cho quet tro toi ten mien do (va ten mien con)
        /// bi vo hieu hoa NGAY, giu doanh thu — tro toi ten mien bi chan la vi pham.
        /// </summary>
        public async Task<BlockedDomainResponse> ChanTenMienAsync(BlockDomainRequest body, Caller caller, CancellationToken ct)
        {
            string tenMien = UrlRules.ChuanHoaTenMienChan(body == null ? null : body.Domain);
            string ly = body == null || body.Reason == null ? string.Empty : body.Reason.Trim();
            if (ly.Length == 0 || ly.Length > 500)
            {
                throw new LinkException(400, "thieu_ly_do", "Ly do chan 1-500 ky tu.");
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            BlockedDomain d = BlockedDomain.Tao(tenMien, ly, caller.UserId, bayGio);
            _db.BlockedDomains.Add(d);

            string duoi = "." + tenMien;
            List<ShortLink> anhHuong = await _db.Links
                .Where(l => (l.Domain == tenMien || l.Domain.EndsWith(duoi))
                            && (l.Status == LinkStatus.Active || l.Status == LinkStatus.PendingScan))
                .ToListAsync(ct);

            foreach (ShortLink l in anhHuong)
            {
                bool dangPhucVu = l.Status == LinkStatus.Active;
                l.VoHieuHoa("Ten mien " + tenMien + " bi chan: " + ly, bayGio);
                if (dangPhucVu)
                {
                    _events.PhatVoHieu(l, true, caller);
                }
            }

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex, "PK_blocked_domains"))
            {
                throw new LinkException(409, "ten_mien_da_chan", "Ten mien da nam trong danh sach chan.");
            }

            return new BlockedDomainResponse { Domain = d.Domain, Reason = d.Reason, CreatedAt = d.CreatedAt, DisabledLinkCount = anhHuong.Count };
        }

        /// <summary>Bo chan: KHONG tu bat lai link da vo hieu hoa (sharer tao link moi).</summary>
        public async Task BoChanTenMienAsync(string domain, CancellationToken ct)
        {
            string tenMien = UrlRules.ChuanHoaTenMienChan(domain);
            BlockedDomain? d = await _db.BlockedDomains.FirstOrDefaultAsync(x => x.Domain == tenMien, ct);
            if (d == null)
            {
                throw new LinkException(404, "khong_tim_thay", "Ten mien khong nam trong danh sach chan.");
            }

            _db.BlockedDomains.Remove(d);
            await _db.SaveChangesAsync(ct);
        }
    }
}
