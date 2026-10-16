using System;
using System.Collections.Generic;
using Crowd.Tasking.Domain.Assignments;

namespace Crowd.Tasking.Tests
{
    public sealed class PhienLamViecTests
    {
        private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);
        private static readonly TimeSpan ToiDa = TimeSpan.FromMinutes(45);
        private static readonly TimeSpan Nghi = TimeSpan.FromMinutes(10);

        private static KhoangLamViec K(int batDauPhut, int ketThucPhut)
        {
            return new KhoangLamViec(T0.AddMinutes(batDauPhut), T0.AddMinutes(ketThucPhut));
        }

        [Fact]
        public void Chua_lam_gi_thi_duoc_nhan()
        {
            Assert.Null(PhienLamViec.PhaiNghiDen(new List<KhoangLamViec>(), T0, ToiDa, Nghi));
        }

        [Fact]
        public void Lam_lien_tuc_du_45_phut_thi_phai_nghi_10_phut_tu_luot_cuoi()
        {
            // 9 luot x 5 phut, cach nhau 0 → 45 phut lien tuc.
            List<KhoangLamViec> ds = new List<KhoangLamViec>();
            for (int i = 0; i < 9; i++)
            {
                ds.Add(K(i * 5, i * 5 + 5));
            }

            DateTimeOffset? den = PhienLamViec.PhaiNghiDen(ds, T0.AddMinutes(45), ToiDa, Nghi);
            Assert.Equal(T0.AddMinutes(55), den);

            // Nghi du 10 phut → phien moi.
            Assert.Null(PhienLamViec.PhaiNghiDen(ds, T0.AddMinutes(55), ToiDa, Nghi));
        }

        [Fact]
        public void Gap_ngan_hon_thoi_gian_nghi_van_tinh_la_lien_tuc()
        {
            // 0-20, nghi 9 phut, 29-50 → mot phien 50 phut.
            List<KhoangLamViec> ds = new List<KhoangLamViec> { K(0, 20), K(29, 50) };
            Assert.Equal(T0.AddMinutes(60), PhienLamViec.PhaiNghiDen(ds, T0.AddMinutes(50), ToiDa, Nghi));
        }

        [Fact]
        public void Gap_du_thoi_gian_nghi_thi_bat_dau_phien_moi()
        {
            // 0-40, nghi 10 phut, 50-60 → phien hien tai chi 10 phut.
            List<KhoangLamViec> ds = new List<KhoangLamViec> { K(0, 40), K(50, 60) };
            Assert.Null(PhienLamViec.PhaiNghiDen(ds, T0.AddMinutes(60), ToiDa, Nghi));
        }

        [Fact]
        public void Thu_tu_dau_vao_khong_quan_trong()
        {
            List<KhoangLamViec> ds = new List<KhoangLamViec> { K(29, 50), K(0, 20) };
            Assert.Equal(T0.AddMinutes(60), PhienLamViec.PhaiNghiDen(ds, T0.AddMinutes(50), ToiDa, Nghi));
        }
    }
}
