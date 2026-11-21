using System;
using System.Collections.Generic;

namespace Crowd.Reco.Domain
{
    /// <summary>Thong so dieu chinh lay tu setting reco.* (khong doc setting trong domain).</summary>
    public sealed class ThongSoGoiY
    {
        public double ChuKyQuenNgay { get; init; } = 90;

        /// <summary>Du an duoc uu tien "moi" cho toi khi bay nhieu labeler phu hop da thay.</summary>
        public long NguongDuAnMoi { get; init; } = 200;

        /// <summary>Cu bay nhieu the co mot the kham pha (0 = tat).</summary>
        public int KhamPhaMoi { get; init; } = 10;

        public int ToiDaMotDoanhNghiep { get; init; } = 2;

        public int ChenMoiMoiTrang { get; init; } = 2;

        public int KichThuocDanhSach { get; init; } = 200;
    }

    /// <summary>Mot du an da cham diem cho mot labeler.</summary>
    public sealed class DuAnDaCham
    {
        public required DuAnGoiY DuAn { get; init; }

        public required double Diem { get; init; }

        public required double PBatDau { get; init; }

        public required double PDat { get; init; }

        /// <summary>Tien ky vong moi phut (dong) — thuong × P(dat) × P(lot suat) / phut.</summary>
        public required double TienMoiPhut { get; init; }

        public required double[] DacTrung { get; init; }

        public required IReadOnlyList<string> LyDo { get; init; }

        /// <summary>Duoc dat vao o kham pha (khong phai theo diem).</summary>
        public bool KhamPha { get; init; }
    }

    /// <summary>
    /// Diem xep feed (muc 5.1 ke hoach):
    ///   tien ky vong = thuong × P(dat) × P(lot suat)
    ///   diem         = P(bat dau) × tien ky vong / phut uoc tinh × he so moi
    /// P(bat dau), P(dat) tu mo hinh (G1 / logistic / LightGBM); P(lot suat), phut uoc tinh tu so lieu du an.
    /// He so moi: du an chua du nguoi thay duoc nhan 1,5 (be luu luong); sau do theo ti le "thay → bat dau" so voi trung
    /// binh cho, chan trong [0,6; 1,4] — khong bao gio ve 0.
    /// </summary>
    public static class ChamDiem
    {
        public const string LyDoKiemTot = "kiem_tot";
        public const string LyDoHopSoThich = "hop_so_thich";
        public const string LyDoHayDat = "hay_dat";
        public const string LyDoMoi = "moi";
        public const string LyDoNhanh = "lam_nhanh";
        public const string LyDoKhamPha = "kham_pha";

        public static DuAnDaCham Cham(
            DuAnGoiY p,
            LabelerGoiY nguoi,
            IMoHinhGoiY moHinh,
            ThongKeCho thongKe,
            ThongSoGoiY thongSo,
            DateTimeOffset luc)
        {
            if (p == null)
            {
                throw new ArgumentNullException(nameof(p));
            }

            if (moHinh == null)
            {
                throw new ArgumentNullException(nameof(moHinh));
            }

            if (thongSo == null)
            {
                throw new ArgumentNullException(nameof(thongSo));
            }

            if (thongKe == null)
            {
                throw new ArgumentNullException(nameof(thongKe));
            }

            double[] x = DacTrung.Tinh(p, nguoi, thongKe, luc, thongSo.ChuKyQuenNgay, thongSo.NguongDuAnMoi);
            double pBatDau = Kep(moHinh.PBatDau(x));
            double pDat = Kep(moHinh.PDat(x));
            double phut = x[DacTrung.EstMinutes];
            double tienMoiPhut = p.RewardVnd * pDat * x[DacTrung.PSlot] / phut;
            bool moi = x[DacTrung.IsNew] > 0;

            double heSo;
            if (moi)
            {
                heSo = 1.5;
            }
            else if (thongKe.TiLeBatDauTrungBinh > 0)
            {
                heSo = Math.Clamp(0.5 + 0.5 * x[DacTrung.ProjectStartRate] / thongKe.TiLeBatDauTrungBinh, 0.6, 1.4);
            }
            else
            {
                heSo = 1;
            }

            double diem = pBatDau * tienMoiPhut * heSo;

            List<string> lyDo = new List<string>();
            if (tienMoiPhut >= 1000)
            {
                lyDo.Add(LyDoKiemTot);
            }

            if (x[DacTrung.Match] >= 0.6)
            {
                lyDo.Add(LyDoHopSoThich);
            }

            if (pDat >= 0.7)
            {
                lyDo.Add(LyDoHayDat);
            }

            if (phut <= 10)
            {
                lyDo.Add(LyDoNhanh);
            }

            if (moi)
            {
                lyDo.Add(LyDoMoi);
            }

            return new DuAnDaCham
            {
                DuAn = p,
                Diem = diem,
                PBatDau = pBatDau,
                PDat = pDat,
                TienMoiPhut = tienMoiPhut,
                DacTrung = x,
                LyDo = lyDo,
            };
        }

        private static double Kep(double p)
        {
            if (double.IsNaN(p))
            {
                return 0.5;
            }

            return Math.Clamp(p, 0.001, 1);
        }
    }
}
