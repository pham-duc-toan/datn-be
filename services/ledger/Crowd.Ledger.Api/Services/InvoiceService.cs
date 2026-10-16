using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Persistence;
using Crowd.BuildingBlocks.Settings;
using Crowd.Ledger.Api.Dtos;
using Crowd.Ledger.Api.Exceptions;
using Crowd.Ledger.Api.Helpers;
using Crowd.Ledger.Domain.Accounts;
using Crowd.Ledger.Domain.Common;
using Crowd.Ledger.Domain.Invoices;
using Crowd.Ledger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crowd.Ledger.Api.Services
{
    /// <summary>
    /// Hoa don GTGT hang thang (FB-05). Moi thang, moi doanh nghiep mot hoa don cho PHI NEN TANG da
    /// thu trong thang — tinh thang tu SO CAI (khong co bang tong hop rieng de lech):
    ///   phi = tong dong platform:fee cua moi but toan trong thang cham vao ky quy cac du an cua doanh
    ///         nghiep (chi tra nhan, chi tra cong link) hoac vi doanh nghiep (hoan tranh chap → am);
    ///   chi ho = tong dong labeler:*:pending cua cung cac but toan do (bang ke, khong chiu VAT).
    /// Hoa thue tranh chap (phi am) gan vao du an cua tranh chap.
    ///
    /// Xuat: doanh nghiep tu bam (POST /ledger/invoices) hoac worker dau thang. Idempotent: UNIQUE
    /// (business, period) — hai ben cung xuat thi mot ben thua va doc lai hoa don cua ben kia. So hoa
    /// don cap tu invoice_counters bang UPDATE ... RETURNING trong CUNG transaction: khong trung, khong
    /// nhay so (transaction rollback thi so cung rollback).
    /// </summary>
    public sealed class InvoiceService
    {
        private static readonly TimeSpan LechVietNam = TimeSpan.FromHours(7);

        private readonly LedgerDbContext _db;
        private readonly IdentityClient _identity;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;
        private readonly ILogger<InvoiceService> _logger;

        public InvoiceService(LedgerDbContext db, IdentityClient identity, ISettings settings, TimeProvider clock, ILogger<InvoiceService> logger)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (identity == null)
            {
                throw new ArgumentNullException(nameof(identity));
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
            _identity = identity;
            _settings = settings;
            _clock = clock;
            _logger = logger;
        }

        public static DateOnly HomNay(DateTimeOffset luc)
        {
            return DateOnly.FromDateTime(luc.ToOffset(LechVietNam).DateTime);
        }

        // =====================================================================
        // XUAT
        // =====================================================================

        /// <summary>Doanh nghiep xuat hoa don thang da ket thuc cua chinh minh. Da co thi tra lai.</summary>
        public async Task<InvoiceResponse> XuatAsync(string? ky, Caller caller, CancellationToken ct)
        {
            Guid businessId = caller.LayUserId();
            string period = Invoice.ChuanHoaKy(ky, HomNay(_clock.GetUtcNow()), _settings.DungSai(SettingKeys.InvoiceAllowOpenPeriod));
            Invoice h = await XuatChoAsync(businessId, period, ct);
            return TaoResponse(h);
        }

        /// <summary>
        /// Tam tinh (KHONG luu, khong cap so): phi + chi ho cua mot thang, ke ca thang dang chay — de
        /// doanh nghiep theo doi truoc khi hoa don duoc xuat.
        /// </summary>
        public async Task<InvoicePreviewResponse> TamTinhAsync(string? ky, Caller caller, CancellationToken ct)
        {
            Guid businessId = caller.LayUserId();
            string period = Invoice.ChuanHoaKy(ky, HomNay(_clock.GetUtcNow()), true);
            DateOnly dau = DateOnly.ParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
            TinhKetQuaThang kq = await TinhThangAsync(businessId, dau, ct);
            int vat = _settings.SoNguyen(SettingKeys.InvoiceVatPercent);

            List<InvoiceItemDto> dong = new List<InvoiceItemDto>();
            foreach (DongPhi d in kq.Phi)
            {
                long chuaThue = Invoice.TachChuaThue(d.PhiDaGomThueVnd, vat);
                dong.Add(new InvoiceItemDto
                {
                    ProjectId = d.ProjectId,
                    Description = d.MoTa,
                    Quantity = d.SoLuong,
                    NetVnd = chuaThue,
                    VatVnd = d.PhiDaGomThueVnd - chuaThue,
                    TotalVnd = d.PhiDaGomThueVnd,
                });
            }

            return new InvoicePreviewResponse
            {
                Period = period,
                VatPercent = vat,
                Items = dong,
                TotalVnd = dong.Sum(x => x.TotalVnd),
                TotalNetVnd = dong.Sum(x => x.NetVnd),
                TotalVatVnd = dong.Sum(x => x.VatVnd),
                PassThroughVnd = kq.ChiHo.Sum(x => x.AmountVnd),
                Issued = await _db.Invoices.AnyAsync(x => x.BusinessId == businessId && x.Period == period, ct),
            };
        }

        /// <summary>
        /// Xuat (hoac lay lai) hoa don cua doanh nghiep cho ky. Khong co phi → 409 khong_co_phi; chua
        /// xac minh KYB → 409 chua_xac_minh_doanh_nghiep (hoa don GTGT can ma so thue nguoi mua).
        /// </summary>
        public async Task<Invoice> XuatChoAsync(Guid businessId, string period, CancellationToken ct)
        {
            Invoice? co = await _db.Invoices.AsNoTracking().FirstOrDefaultAsync(x => x.BusinessId == businessId && x.Period == period, ct);
            if (co != null)
            {
                return co;
            }

            DateOnly dau = DateOnly.ParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
            List<DongPhi> phi;
            List<PassThroughItem> chiHo;
            TinhKetQuaThang kq = await TinhThangAsync(businessId, dau, ct);
            phi = kq.Phi;
            chiHo = kq.ChiHo;
            if (phi.Sum(p => p.PhiDaGomThueVnd) <= 0)
            {
                throw new RuleViolationException("khong_co_phi", "Thang " + period + " khong co phi nen tang nao de xuat hoa don.");
            }

            HoSoDoanhNghiep? hs = await _identity.LayDoanhNghiepAsync(businessId, ct);
            if (hs == null || hs.Status != "verified")
            {
                throw new RuleViolationException("chua_xac_minh_doanh_nghiep", "Doanh nghiep chua duoc xac minh (KYB) — hoa don GTGT can ten va ma so thue da xac minh.");
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            int nam = HomNay(bayGio).Year;
            string serial = _settings.Chuoi(SettingKeys.InvoiceSerialTemplate)
                .Replace("{yy}", (nam % 100).ToString("D2", CultureInfo.InvariantCulture), StringComparison.Ordinal);

            var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                int so = await CapSoAsync(nam, ct);
                Invoice h = Invoice.Xuat(
                    businessId,
                    period,
                    serial,
                    so,
                    new NguoiMua { Ten = hs.CompanyName, MaSoThue = hs.TaxCode, DiaChi = hs.Address, Email = hs.Email },
                    new NguoiBan
                    {
                        Ten = _settings.Chuoi(SettingKeys.InvoiceSellerName),
                        MaSoThue = _settings.Chuoi(SettingKeys.InvoiceSellerTaxCode),
                        DiaChi = _settings.Chuoi(SettingKeys.InvoiceSellerAddress),
                    },
                    _settings.SoNguyen(SettingKeys.InvoiceVatPercent),
                    phi,
                    chiHo,
                    bayGio);

                _db.Invoices.Add(h);
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                _logger.LogInformation("Xuat hoa don {Serial}-{So} ky {Ky} cho doanh nghiep {BusinessId}: {Tong}d",
                    serial, so, period, businessId, h.TotalVnd);
                return h;
            }
            catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex, "ux_invoices_business_period"))
            {
                // Hai yeu cau cung xuat mot ky: ben thua rollback (so hoa don cung rollback), doc ban cua ben thang.
                await tx.RollbackAsync(ct);
                _db.ChangeTracker.Clear();
                return await _db.Invoices.AsNoTracking().FirstAsync(x => x.BusinessId == businessId && x.Period == period, ct);
            }
            finally
            {
                await tx.DisposeAsync();
            }
        }

        /// <summary>
        /// Worker: xuat hoa don THANG TRUOC cho moi doanh nghiep co phi ma chua co hoa don. Doanh nghiep
        /// chua KYB thi bo qua (tu xuat sau khi xac minh). Tra so hoa don vua xuat.
        /// </summary>
        public async Task<int> TuXuatThangTruocAsync(CancellationToken ct)
        {
            DateOnly homNay = HomNay(_clock.GetUtcNow());
            DateOnly dauThangTruoc = new DateOnly(homNay.Year, homNay.Month, 1).AddMonths(-1);
            string period = dauThangTruoc.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            DateTimeOffset tu = DauNgayVietNam(dauThangTruoc);
            DateTimeOffset den = DauNgayVietNam(dauThangTruoc.AddMonths(1));

            List<Guid> coPhi = await _db.Database.SqlQuery<Guid>($@"
                SELECT DISTINCT pe.owner_id AS ""Value""
                FROM journal_entries e
                JOIN journal_lines f ON f.entry_id = e.id AND f.account_code = 'platform:fee'
                JOIN journal_lines x ON x.entry_id = e.id AND x.account_code LIKE 'escrow:project:%'
                JOIN project_escrows pe ON 'escrow:project:' || pe.project_id = x.account_code
                WHERE e.created_at >= {tu} AND e.created_at < {den}
                  AND NOT EXISTS (SELECT 1 FROM invoices i WHERE i.business_id = pe.owner_id AND i.period = {period})")
                .ToListAsync(ct);

            int daXuat = 0;
            foreach (Guid b in coPhi)
            {
                try
                {
                    await XuatChoAsync(b, period, ct);
                    daXuat++;
                }
                catch (RuleViolationException ex)
                {
                    _logger.LogInformation("Bo qua tu xuat hoa don {Ky} cho {BusinessId}: {Ma}", period, b, ex.Code);
                }
            }

            return daXuat;
        }

        // =====================================================================
        // DOC
        // =====================================================================

        public async Task<List<InvoiceResponse>> CuaToiAsync(Caller caller, CancellationToken ct)
        {
            Guid uid = caller.LayUserId();
            List<Invoice> ds = await _db.Invoices.AsNoTracking().Where(x => x.BusinessId == uid).OrderByDescending(x => x.Period).ToListAsync(ct);
            return ds.Select(TaoResponse).ToList();
        }

        public async Task<PagedResponse<InvoiceResponse>> AdminDanhSachAsync(string? period, int page, int pageSize, CancellationToken ct)
        {
            int trang = page < 1 ? 1 : page;
            int co = pageSize < 1 || pageSize > 100 ? 20 : pageSize;
            IQueryable<Invoice> q = _db.Invoices.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(period))
            {
                string p = period.Trim();
                q = q.Where(x => x.Period == p);
            }

            int tong = await q.CountAsync(ct);
            List<Invoice> ds = await q.OrderByDescending(x => x.IssuedAt).Skip((trang - 1) * co).Take(co).ToListAsync(ct);
            return new PagedResponse<InvoiceResponse> { Items = ds.Select(TaoResponse).ToList(), Page = trang, PageSize = co, Total = tong };
        }

        /// <summary>Chu hoa don hoac admin; nguoi khac → 404 (BOLA).</summary>
        public async Task<Invoice> LayAsync(Guid id, Caller caller, CancellationToken ct)
        {
            Invoice? h = await _db.Invoices.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (h == null || (h.BusinessId != caller.LayUserId() && !caller.IsAdmin))
            {
                throw new NotFoundException("Khong tim thay hoa don.");
            }

            return h;
        }

        // =====================================================================

        private sealed class TinhKetQuaThang
        {
            public required List<DongPhi> Phi { get; init; }

            public required List<PassThroughItem> ChiHo { get; init; }
        }

        private sealed class DongTongHop
        {
            public Guid? ProjectId { get; set; }

            public long Phi { get; set; }

            public long ChiHo { get; set; }

            public long SoLuot { get; set; }
        }

        private async Task<TinhKetQuaThang> TinhThangAsync(Guid businessId, DateOnly dauThang, CancellationToken ct)
        {
            DateTimeOffset tu = DauNgayVietNam(dauThang);
            DateTimeOffset den = DauNgayVietNam(dauThang.AddMonths(1));
            string viDoanhNghiep = AccountCodes.BusinessAvailable(businessId);

            // Moi but toan trong thang cham ky quy cac du an cua doanh nghiep hoac vi doanh nghiep; du an
            // cua but toan = du an co dong ky quy, khong co thi du an cua tranh chap (hoan tien ve vi).
            List<DongTongHop> dong = await _db.Database.SqlQuery<DongTongHop>($@"
                WITH du_an AS (
                    SELECT project_id, 'escrow:project:' || project_id AS ma FROM project_escrows WHERE owner_id = {businessId}
                ), but_toan AS (
                    SELECT DISTINCT e.id, e.reference
                    FROM journal_entries e JOIN journal_lines l ON l.entry_id = e.id
                    WHERE e.created_at >= {tu} AND e.created_at < {den}
                      AND (l.account_code IN (SELECT ma FROM du_an) OR l.account_code = {viDoanhNghiep})
                ), gan AS (
                    SELECT b.id,
                           COALESCE(
                               (SELECT d.project_id FROM journal_lines x JOIN du_an d ON d.ma = x.account_code WHERE x.entry_id = b.id LIMIT 1),
                               (SELECT t.project_id FROM disputes t WHERE 'dispute:' || t.id = b.reference)) AS project_id
                    FROM but_toan b
                )
                SELECT g.project_id AS ""ProjectId"",
                       COALESCE(SUM(CASE WHEN l.account_code = 'platform:fee' THEN l.amount ELSE 0 END), 0)::bigint AS ""Phi"",
                       COALESCE(SUM(CASE WHEN l.account_code LIKE 'labeler:%:pending' THEN l.amount ELSE 0 END), 0)::bigint AS ""ChiHo"",
                       COUNT(DISTINCT CASE WHEN l.account_code = 'platform:fee' AND l.amount > 0 THEN g.id END)::bigint AS ""SoLuot""
                FROM gan g JOIN journal_lines l ON l.entry_id = g.id
                GROUP BY g.project_id").ToListAsync(ct);

            List<Guid> ids = dong.Where(d => d.ProjectId.HasValue).Select(d => d.ProjectId!.Value).ToList();
            Dictionary<Guid, string?> ten = await _db.Escrows.AsNoTracking()
                .Where(e => ids.Contains(e.ProjectId))
                .ToDictionaryAsync(e => e.ProjectId, e => e.Name, ct);

            List<DongPhi> phi = new List<DongPhi>();
            List<PassThroughItem> chiHo = new List<PassThroughItem>();
            foreach (DongTongHop d in dong.OrderBy(x => x.ProjectId))
            {
                string tenDuAn = TenDuAn(d.ProjectId, ten);
                if (d.Phi != 0)
                {
                    phi.Add(new DongPhi
                    {
                        ProjectId = d.ProjectId,
                        MoTa = "Phí dịch vụ nền tảng gán nhãn dữ liệu — " + tenDuAn,
                        SoLuong = d.SoLuot,
                        PhiDaGomThueVnd = d.Phi,
                    });
                }

                if (d.ChiHo != 0)
                {
                    chiHo.Add(new PassThroughItem
                    {
                        ProjectId = d.ProjectId,
                        Description = "Thù lao chi hộ người gán nhãn / chia sẻ link — " + tenDuAn,
                        AmountVnd = d.ChiHo,
                    });
                }
            }

            return new TinhKetQuaThang { Phi = phi, ChiHo = chiHo };
        }

        private static string TenDuAn(Guid? projectId, Dictionary<Guid, string?> ten)
        {
            if (!projectId.HasValue)
            {
                return "điều chỉnh";
            }

            string? t;
            if (ten.TryGetValue(projectId.Value, out t) && !string.IsNullOrWhiteSpace(t))
            {
                return "dự án " + t;
            }

            return "dự án " + projectId.Value.ToString("N").Remove(8);
        }

        /// <summary>Cap so ke tiep cua nam (tao dong nam neu chua co). Goi TRONG transaction.</summary>
        private async Task<int> CapSoAsync(int nam, CancellationToken ct)
        {
            List<int> so = await _db.Database.SqlQuery<int>($@"
                INSERT INTO invoice_counters (year, last_number) VALUES ({nam}, 1)
                ON CONFLICT (year) DO UPDATE SET last_number = invoice_counters.last_number + 1
                RETURNING last_number AS ""Value""").ToListAsync(ct);
            return so[0];
        }

        private static DateTimeOffset DauNgayVietNam(DateOnly ngay)
        {
            return new DateTimeOffset(ngay.Year, ngay.Month, ngay.Day, 0, 0, 0, LechVietNam).ToUniversalTime();
        }

        public static InvoiceResponse TaoResponse(Invoice h)
        {
            return new InvoiceResponse
            {
                InvoiceId = h.Id,
                BusinessId = h.BusinessId,
                Period = h.Period,
                Serial = h.Serial,
                Number = h.SoHienThi,
                IssuedAt = h.IssuedAt,
                BuyerName = h.BuyerName,
                BuyerTaxCode = h.BuyerTaxCode,
                BuyerAddress = h.BuyerAddress,
                SellerName = h.SellerName,
                SellerTaxCode = h.SellerTaxCode,
                SellerAddress = h.SellerAddress,
                VatPercent = h.VatPercent,
                TotalNetVnd = h.TotalNetVnd,
                TotalVatVnd = h.TotalVatVnd,
                TotalVnd = h.TotalVnd,
                AmountInWords = h.AmountInWords,
                Items = h.Items.Select(i => new InvoiceItemDto
                {
                    ProjectId = i.ProjectId,
                    Description = i.Description,
                    Quantity = i.Quantity,
                    NetVnd = i.NetVnd,
                    VatVnd = i.VatVnd,
                    TotalVnd = i.TotalVnd,
                }).ToList(),
                PassThrough = h.PassThrough.Select(p => new PassThroughItemDto
                {
                    ProjectId = p.ProjectId,
                    Description = p.Description,
                    AmountVnd = p.AmountVnd,
                }).ToList(),
                PassThroughVnd = h.PassThroughVnd,
            };
        }
    }
}
