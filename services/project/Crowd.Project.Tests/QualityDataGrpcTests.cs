using System;
using System.Collections.Generic;
using Crowd.Project.Api.Grpc;

namespace Crowd.Project.Tests
{
    /// <summary>
    /// Ham dung chung cua gRPC QualityData (kien truc 3.22). "Giay moi cau" phai CUNG cong thuc voi quality-svc
    /// (project_client.Bai.giay_moi_cau): quality xet "qua nhanh" theo trung vi du an, admin xem trung vi theo
    /// labeler tu RPC nay — hai so lech cong thuc thi ho so gian lan noi mot dang, bang chung noi mot neo.
    /// </summary>
    public sealed class QualityDataGrpcTests
    {
        private static readonly DateTimeOffset Luc = new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

        [Fact]
        public void GiayMoiCau_chia_cho_so_cau_da_tra_loi()
        {
            Assert.Equal(10.0, QualityDataService.GiayMoiCau(Luc, Luc.AddSeconds(40), 4));
        }

        [Fact]
        public void GiayMoiCau_khong_tra_loi_cau_nao_thi_chia_cho_mot_va_khong_am()
        {
            Assert.Equal(40.0, QualityDataService.GiayMoiCau(Luc, Luc.AddSeconds(40), 0));
            Assert.Equal(0.0, QualityDataService.GiayMoiCau(Luc, Luc.AddSeconds(-5), 3));
        }

        [Fact]
        public void TrungVi_le_chan_va_rong()
        {
            Assert.Equal(3.0, QualityDataService.TrungVi(new List<double> { 1, 3, 9 }));
            Assert.Equal(5.0, QualityDataService.TrungVi(new List<double> { 1, 3, 7, 9 }));
            Assert.Equal(0.0, QualityDataService.TrungVi(new List<double>()));
        }
    }
}
