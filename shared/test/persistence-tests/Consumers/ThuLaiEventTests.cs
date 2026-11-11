using System;
using System.Collections.Generic;
using System.Text;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Xunit;

namespace Crowd.BuildingBlocks.Persistence.Tests.Consumers
{
    /// <summary>Loai 3 (kien truc 3.29): dem lan giao, nhan ra lan cuoi, gian cach thu lai.</summary>
    public sealed class ThuLaiEventTests
    {
        [Fact]
        public void Doc_x_delivery_count_moi_kieu_header()
        {
            Assert.Equal(0L, ThuLaiEvent.SoLanDaThatBai(null));
            Assert.Equal(0L, ThuLaiEvent.SoLanDaThatBai(new Dictionary<string, object?>()));
            Assert.Equal(3L, ThuLaiEvent.SoLanDaThatBai(new Dictionary<string, object?> { { "x-delivery-count", 3L } }));
            Assert.Equal(2L, ThuLaiEvent.SoLanDaThatBai(new Dictionary<string, object?> { { "x-delivery-count", 2 } }));
            Assert.Equal(4L, ThuLaiEvent.SoLanDaThatBai(new Dictionary<string, object?> { { "x-delivery-count", Encoding.UTF8.GetBytes("4") } }));
        }

        [Fact]
        public void Lan_cuoi_la_lan_giao_thu_gioi_han_cong_mot()
        {
            // x-delivery-limit = 5: broker dead-letter khi tra lai lan thu 6 → lan giao co x-delivery-count = 5 la lan cuoi.
            Assert.False(ThuLaiEvent.LaLanCuoi(0, 5));
            Assert.False(ThuLaiEvent.LaLanCuoi(4, 5));
            Assert.True(ThuLaiEvent.LaLanCuoi(5, 5));
            Assert.True(ThuLaiEvent.LaLanCuoi(9, 5));
        }

        [Fact]
        public void Gian_cach_tang_gap_ba_va_co_tran()
        {
            TimeSpan toiDa = TimeSpan.FromSeconds(30);
            Assert.Equal(TimeSpan.FromSeconds(1), ThuLaiEvent.ChoTruocKhiThuLai(0, toiDa));
            Assert.Equal(TimeSpan.FromSeconds(3), ThuLaiEvent.ChoTruocKhiThuLai(1, toiDa));
            Assert.Equal(TimeSpan.FromSeconds(9), ThuLaiEvent.ChoTruocKhiThuLai(2, toiDa));
            Assert.Equal(TimeSpan.FromSeconds(27), ThuLaiEvent.ChoTruocKhiThuLai(3, toiDa));
            Assert.Equal(toiDa, ThuLaiEvent.ChoTruocKhiThuLai(4, toiDa));
            Assert.Equal(toiDa, ThuLaiEvent.ChoTruocKhiThuLai(1000, toiDa));
            Assert.Equal(TimeSpan.Zero, ThuLaiEvent.ChoTruocKhiThuLai(2, TimeSpan.Zero));
        }

        [Fact]
        public void Tien_viet_cho_mo_ta()
        {
            Assert.Equal("500.000 đ", ViecCanXuLyTay.Tien(500000));
        }
    }
}
