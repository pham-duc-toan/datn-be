using System;
using System.Collections.Generic;
using System.Globalization;
using Crowd.Ledger.Domain.Common;

namespace Crowd.Ledger.Domain.Invoices
{
    /// <summary>
    /// Hoa don GTGT (FB-05) cua MOT doanh nghiep cho MOT thang. Nen tang thu doanh nghiep hai khoan:
    ///   - thu lao labeler / sharer: tien CHI HO, khong phai doanh thu nen tang → KHONG xuat hoa don GTGT,
    ///     chi liet ke o bang ke (PassThrough) de doanh nghiep doi chieu;
    ///   - phi nen tang (fee.platform_percent): DOANH THU dich vu → xuat hoa don. Phi da thu la gia DA GOM
    ///     thue (doanh nghiep thay mot con so), hoa don tach nguoc: chua thue = round(phi x 100 / (100 + VAT%)),
    ///     thue = phi - chua thue — tong luon khop dung so tien da tru trong so cai.
    ///
    /// BAT BIEN sau khi xuat: thong tin nguoi mua / nguoi ban / dong hang duoc CHUP luc xuat; doanh nghiep
    /// doi ten sau nay khong lam doi hoa don cu. Sai thi xuat hoa don dieu chinh (chua lam), khong sua.
    /// UNIQUE (business_id, period): moi thang mot hoa don.
    /// </summary>
    public sealed class Invoice
    {
        private List<InvoiceItem> _items;
        private List<PassThroughItem> _passThrough;

        private Invoice()
        {
            Period = string.Empty;
            Serial = string.Empty;
            BuyerName = string.Empty;
            BuyerTaxCode = string.Empty;
            BuyerAddress = string.Empty;
            BuyerEmail = string.Empty;
            SellerName = string.Empty;
            SellerTaxCode = string.Empty;
            SellerAddress = string.Empty;
            AmountInWords = string.Empty;
            _items = new List<InvoiceItem>();
            _passThrough = new List<PassThroughItem>();
        }

        public Guid Id { get; private set; }

        public Guid BusinessId { get; private set; }

        /// <summary>Thang "yyyy-MM" (gio Viet Nam).</summary>
        public string Period { get; private set; }

        /// <summary>Ky hieu hoa don, vd 1C26TDN (1 = GTGT, C = co ma CQT, 26 = nam, T = dien tu, DN = nen tang).</summary>
        public string Serial { get; private set; }

        /// <summary>So hoa don, tang dan trong nam, bat dau tu 1.</summary>
        public int Number { get; private set; }

        public DateTimeOffset IssuedAt { get; private set; }

        public string BuyerName { get; private set; }

        public string BuyerTaxCode { get; private set; }

        public string BuyerAddress { get; private set; }

        public string BuyerEmail { get; private set; }

        public string SellerName { get; private set; }

        public string SellerTaxCode { get; private set; }

        public string SellerAddress { get; private set; }

        public int VatPercent { get; private set; }

        public long TotalNetVnd { get; private set; }

        public long TotalVatVnd { get; private set; }

        public long TotalVnd { get; private set; }

        /// <summary>Tong thanh toan bang chu (bat buoc tren hoa don).</summary>
        public string AmountInWords { get; private set; }

        /// <summary>Tong tien chi ho labeler / sharer trong thang (bang ke, khong chiu VAT).</summary>
        public long PassThroughVnd { get; private set; }

        public IReadOnlyList<InvoiceItem> Items
        {
            get { return _items; }
        }

        public IReadOnlyList<PassThroughItem> PassThrough
        {
            get { return _passThrough; }
        }

        /// <summary>So hien thi "0000123" (7 chu so nhu mau hoa don).</summary>
        public string SoHienThi
        {
            get { return Number.ToString("D7", CultureInfo.InvariantCulture); }
        }

        /// <param name="phiTheoDuAn">Phi nen tang (DA gom VAT) theo tung du an trong thang; khoa null = dieu chinh khong gan du an.</param>
        public static Invoice Xuat(
            Guid businessId,
            string period,
            string serial,
            int number,
            NguoiMua nguoiMua,
            NguoiBan nguoiBan,
            int vatPercent,
            IReadOnlyList<DongPhi> phiTheoDuAn,
            IReadOnlyList<PassThroughItem> chiHo,
            DateTimeOffset luc)
        {
            if (nguoiMua == null)
            {
                throw new ArgumentNullException(nameof(nguoiMua));
            }

            if (nguoiBan == null)
            {
                throw new ArgumentNullException(nameof(nguoiBan));
            }

            if (phiTheoDuAn == null)
            {
                throw new ArgumentNullException(nameof(phiTheoDuAn));
            }

            if (chiHo == null)
            {
                throw new ArgumentNullException(nameof(chiHo));
            }

            if (vatPercent < 0 || vatPercent > 100)
            {
                throw new InvalidValueException("vat_khong_hop_le", "Thuế suất GTGT 0–100%.");
            }

            Invoice h = new Invoice();
            h.Id = Guid.CreateVersion7();
            h.BusinessId = businessId;
            h.Period = period;
            h.Serial = serial;
            h.Number = number;
            h.IssuedAt = luc;
            h.BuyerName = nguoiMua.Ten;
            h.BuyerTaxCode = nguoiMua.MaSoThue;
            h.BuyerAddress = nguoiMua.DiaChi;
            h.BuyerEmail = nguoiMua.Email;
            h.SellerName = nguoiBan.Ten;
            h.SellerTaxCode = nguoiBan.MaSoThue;
            h.SellerAddress = nguoiBan.DiaChi;
            h.VatPercent = vatPercent;

            foreach (DongPhi d in phiTheoDuAn)
            {
                if (d.PhiDaGomThueVnd == 0)
                {
                    continue;
                }

                long chuaThue = TachChuaThue(d.PhiDaGomThueVnd, vatPercent);
                h._items.Add(new InvoiceItem
                {
                    ProjectId = d.ProjectId,
                    Description = d.MoTa,
                    Quantity = d.SoLuong,
                    NetVnd = chuaThue,
                    VatVnd = d.PhiDaGomThueVnd - chuaThue,
                    TotalVnd = d.PhiDaGomThueVnd,
                });
                h.TotalNetVnd += chuaThue;
                h.TotalVatVnd += d.PhiDaGomThueVnd - chuaThue;
                h.TotalVnd += d.PhiDaGomThueVnd;
            }

            if (h.TotalVnd <= 0)
            {
                throw new RuleViolationException("khong_co_phi", "Tháng " + period + " không có phí nền tảng nào để xuất hoá đơn.");
            }

            foreach (PassThroughItem p in chiHo)
            {
                h._passThrough.Add(p);
                h.PassThroughVnd += p.AmountVnd;
            }

            h.AmountInWords = DocSoTien.BangChu(h.TotalVnd);
            return h;
        }

        /// <summary>Gia chua thue tu gia da gom thue, lam tron toi dong gan nhat (0,5 lam tron len).</summary>
        public static long TachChuaThue(long daGomThue, int vatPercent)
        {
            if (vatPercent == 0)
            {
                return daGomThue;
            }

            decimal chuaThue = (decimal)daGomThue * 100m / (100m + vatPercent);
            return (long)Math.Round(chuaThue, MidpointRounding.AwayFromZero);
        }

        /// <summary>Kiem va chuan hoa ky "yyyy-MM".</summary>
        public static string ChuanHoaKy(string? ky, DateOnly homNay)
        {
            return ChuanHoaKy(ky, homNay, false);
        }

        /// <param name="choPhepKyMo">Cho thang dang chay (xem tam tinh; setting kiem thu invoice.allow_open_period).</param>
        public static string ChuanHoaKy(string? ky, DateOnly homNay, bool choPhepKyMo)
        {
            DateOnly dau;
            if (string.IsNullOrWhiteSpace(ky)
                || !DateOnly.TryParseExact(ky.Trim() + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dau))
            {
                throw new InvalidValueException("ky_khong_hop_le", "Kỳ hoá đơn dạng yyyy-MM, vd 2026-09.");
            }

            DateOnly dauThangNay = new DateOnly(homNay.Year, homNay.Month, 1);
            if (dau > dauThangNay || (dau == dauThangNay && !choPhepKyMo))
            {
                throw new RuleViolationException("ky_chua_ket_thuc", "Chỉ xuất hoá đơn cho tháng ĐÃ kết thúc.");
            }

            return dau.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Mot dong hang hoa / dich vu tren hoa don (luu jsonb).</summary>
    public sealed class InvoiceItem
    {
        public Guid? ProjectId { get; init; }

        public required string Description { get; init; }

        /// <summary>So luot tinh phi (nhan duoc duyet + luot vuot link) trong thang; 0 voi dong dieu chinh.</summary>
        public required long Quantity { get; init; }

        public required long NetVnd { get; init; }

        public required long VatVnd { get; init; }

        public required long TotalVnd { get; init; }
    }

    /// <summary>Mot dong bang ke tien chi ho (thu lao labeler / sharer) — khong chiu VAT.</summary>
    public sealed class PassThroughItem
    {
        public Guid? ProjectId { get; init; }

        public required string Description { get; init; }

        public required long AmountVnd { get; init; }
    }

    /// <summary>Phi nen tang (da gom VAT) cua mot du an trong thang — dau vao de xuat hoa don.</summary>
    public sealed class DongPhi
    {
        public Guid? ProjectId { get; init; }

        public required string MoTa { get; init; }

        public required long SoLuong { get; init; }

        public required long PhiDaGomThueVnd { get; init; }
    }

    public sealed class NguoiMua
    {
        public required string Ten { get; init; }

        public required string MaSoThue { get; init; }

        public required string DiaChi { get; init; }

        public required string Email { get; init; }
    }

    public sealed class NguoiBan
    {
        public required string Ten { get; init; }

        public required string MaSoThue { get; init; }

        public required string DiaChi { get; init; }
    }

    /// <summary>Bo dem so hoa don theo nam (invoice_counters): cap so bang UPDATE ... RETURNING, khong trung khong nhay.</summary>
    public sealed class InvoiceCounter
    {
        private InvoiceCounter()
        {
        }

        public int Year { get; private set; }

        public int LastNumber { get; private set; }
    }
}
