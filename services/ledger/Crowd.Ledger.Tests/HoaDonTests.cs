using System;
using System.Collections.Generic;
using Crowd.Ledger.Domain.Common;
using Crowd.Ledger.Domain.Invoices;

namespace Crowd.Ledger.Tests
{
    public sealed class HoaDonTests
    {
        private static readonly DateTimeOffset T = new DateTimeOffset(2026, 10, 14, 3, 0, 0, TimeSpan.Zero);

        private static readonly NguoiMua Mua = new NguoiMua { Ten = "Cong ty A", MaSoThue = "0312345678", DiaChi = "Ha Noi", Email = "a@b.c" };
        private static readonly NguoiBan Ban = new NguoiBan { Ten = "Nen tang", MaSoThue = "0109999999", DiaChi = "Ha Noi" };

        [Theory]
        [InlineData(0L, "Không đồng")]
        [InlineData(5L, "Năm đồng")]
        [InlineData(15L, "Mười lăm đồng")]
        [InlineData(21L, "Hai mươi mốt đồng")]
        [InlineData(105L, "Một trăm linh năm đồng")]
        [InlineData(50000L, "Năm mươi nghìn đồng")]
        [InlineData(1005000L, "Một triệu không trăm linh năm nghìn đồng")]
        [InlineData(21415000L, "Hai mươi mốt triệu bốn trăm mười lăm nghìn đồng")]
        [InlineData(1000000000L, "Một tỷ đồng")]
        [InlineData(2000500000000L, "Hai nghìn tỷ năm trăm triệu đồng")]
        [InlineData(124L, "Một trăm hai mươi tư đồng")]
        public void Doc_so_tien_bang_chu(long so, string chu)
        {
            Assert.Equal(chu, DocSoTien.BangChu(so));
        }

        [Theory]
        [InlineData(110000L, 10, 100000L)]
        [InlineData(60000L, 10, 54545L)]
        [InlineData(60000L, 0, 60000L)]
        [InlineData(1L, 10, 1L)]
        public void Tach_gia_chua_thue_tu_gia_da_gom_thue(long daGom, int vat, long chuaThue)
        {
            Assert.Equal(chuaThue, Invoice.TachChuaThue(daGom, vat));
        }

        [Fact]
        public void Xuat_cong_dung_tung_dong_va_tong_khop_so_da_thu()
        {
            Guid p1 = Guid.NewGuid();
            Guid p2 = Guid.NewGuid();
            List<DongPhi> phi = new List<DongPhi>
            {
                new DongPhi { ProjectId = p1, MoTa = "Phi du an 1", SoLuong = 3, PhiDaGomThueVnd = 60000 },
                new DongPhi { ProjectId = p2, MoTa = "Phi du an 2", SoLuong = 1, PhiDaGomThueVnd = 33333 },
                new DongPhi { ProjectId = null, MoTa = "Dieu chinh", SoLuong = 0, PhiDaGomThueVnd = -6000 },
            };
            List<PassThroughItem> chiHo = new List<PassThroughItem>
            {
                new PassThroughItem { ProjectId = p1, Description = "Chi ho 1", AmountVnd = 200000 },
            };

            Invoice h = Invoice.Xuat(Guid.NewGuid(), "2026-09", "1C26TDN", 7, Mua, Ban, 10, phi, chiHo, T);

            Assert.Equal(3, h.Items.Count);
            Assert.Equal(87333, h.TotalVnd);
            Assert.Equal(h.TotalVnd, h.TotalNetVnd + h.TotalVatVnd);
            foreach (InvoiceItem i in h.Items)
            {
                Assert.Equal(i.TotalVnd, i.NetVnd + i.VatVnd);
            }

            Assert.Equal(200000, h.PassThroughVnd);
            Assert.Equal("0000007", h.SoHienThi);
            Assert.Equal("Tám mươi bảy nghìn ba trăm ba mươi ba đồng", h.AmountInWords);
        }

        [Fact]
        public void Khong_co_phi_thi_khong_xuat()
        {
            Assert.Equal("khong_co_phi", Assert.Throws<RuleViolationException>(() => Invoice.Xuat(
                Guid.NewGuid(), "2026-09", "1C26TDN", 1, Mua, Ban, 10, new List<DongPhi>(), new List<PassThroughItem>(), T)).Code);
        }

        [Fact]
        public void Ky_phai_la_thang_da_ket_thuc()
        {
            DateOnly homNay = new DateOnly(2026, 10, 14);
            Assert.Equal("2026-09", Invoice.ChuanHoaKy(" 2026-09 ", homNay));
            Assert.Equal("ky_chua_ket_thuc", Assert.Throws<RuleViolationException>(() => Invoice.ChuanHoaKy("2026-10", homNay)).Code);
            Assert.Equal("ky_khong_hop_le", Assert.Throws<InvalidValueException>(() => Invoice.ChuanHoaKy("2026/09", homNay)).Code);
            Assert.Equal("ky_khong_hop_le", Assert.Throws<InvalidValueException>(() => Invoice.ChuanHoaKy("2026-13", homNay)).Code);
        }
    }
}
