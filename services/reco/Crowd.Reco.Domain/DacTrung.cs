using System;
using System.Collections.Generic;
using System.Linq;

namespace Crowd.Reco.Domain
{
    /// <summary>
    /// So lieu CHUNG cua ca cho (tinh moi lan nap bo nho, khong theo nguoi): thoi gian moi cau theo loai cong cu, ti le
    /// "thay → bat dau" trung binh. Dung lam gia tri thay the khi mot du an chua co du lieu.
    /// </summary>
    public sealed class ThongKeCho
    {
        public const double GiayMoiCauMacDinh = 20;

        /// <summary>Ti le bai nop dat trung binh khi uoc so doi thu se dat (chua co du lieu that).</summary>
        public const double TiLeDatChung = 0.6;

        public ThongKeCho(IReadOnlyDictionary<string, double> giayMoiCauTheoCongCu, double tiLeBatDauTrungBinh)
        {
            if (giayMoiCauTheoCongCu == null)
            {
                throw new ArgumentNullException(nameof(giayMoiCauTheoCongCu));
            }

            GiayMoiCauTheoCongCu = giayMoiCauTheoCongCu;
            TiLeBatDauTrungBinh = tiLeBatDauTrungBinh;
        }

        public IReadOnlyDictionary<string, double> GiayMoiCauTheoCongCu { get; }

        public double TiLeBatDauTrungBinh { get; }

        public static ThongKeCho Rong()
        {
            return new ThongKeCho(new Dictionary<string, double>(), 0);
        }

        /// <summary>Tinh tu danh sach du an: trung vi giay moi cau cua cac du an co du lieu, theo tung loai cong cu.</summary>
        public static ThongKeCho Tinh(IEnumerable<DuAnGoiY> duAn)
        {
            if (duAn == null)
            {
                throw new ArgumentNullException(nameof(duAn));
            }

            Dictionary<string, List<double>> theoCongCu = new Dictionary<string, List<double>>(StringComparer.Ordinal);
            long thay = 0;
            long batDau = 0;
            foreach (DuAnGoiY p in duAn)
            {
                if (p.SeenCount > 0)
                {
                    thay += p.SeenCount;
                    batDau += p.StartedCount;
                }

                if (!p.MedianSecondsPerQuestion.HasValue)
                {
                    continue;
                }

                foreach (string cc in p.ToolKinds)
                {
                    List<double>? ds;
                    if (!theoCongCu.TryGetValue(cc, out ds))
                    {
                        ds = new List<double>();
                        theoCongCu[cc] = ds;
                    }

                    ds.Add(p.MedianSecondsPerQuestion.Value);
                }
            }

            Dictionary<string, double> trungVi = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, List<double>> cap in theoCongCu)
            {
                List<double> sx = cap.Value.OrderBy(x => x).ToList();
                trungVi[cap.Key] = sx[sx.Count / 2];
            }

            double tiLe = thay == 0 ? 0 : Math.Min(1, (double)batDau / thay);
            return new ThongKeCho(trungVi, tiLe);
        }

        /// <summary>Giay moi cau uoc tinh cua mot du an.</summary>
        public double GiayMoiCau(DuAnGoiY p)
        {
            if (p == null)
            {
                throw new ArgumentNullException(nameof(p));
            }

            if (p.MedianSecondsPerQuestion.HasValue && p.MedianSecondsPerQuestion.Value > 0)
            {
                return p.MedianSecondsPerQuestion.Value;
            }

            double max = 0;
            foreach (string cc in p.ToolKinds)
            {
                double g;
                if (GiayMoiCauTheoCongCu.TryGetValue(cc, out g) && g > max)
                {
                    max = g;
                }
            }

            return max > 0 ? max : GiayMoiCauMacDinh;
        }
    }

    /// <summary>
    /// Vector dac trung cua MOT cap (labeler, du an) tai luc xep feed — CUNG thu tu cho cong thuc G1, hoi quy logistic va
    /// LightGBM (ml-svc hoc tren dung vector nay, ghi vao ClickHouse luc tra feed). Them dac trung = them o CUOI.
    /// Chi dung thong tin co TRUOC luc hien the (khong dung ket qua cuoi cua du an).
    /// </summary>
    public static class DacTrung
    {
        public static readonly IReadOnlyList<string> Ten = new string[]
        {
            "log_reward",            // 0  ln(1 + thuong / 1000)
            "slots",                 // 1
            "competitors",           // 2  bai nop + dang lam
            "p_slot",                // 3  min(1, suat / (1 + doi thu du kien dat))
            "est_minutes",           // 4  so cau × giay moi cau / 60
            "question_count",        // 5
            "match",                 // 6  trung binh muc hop 4 loai the
            "match_domain",          // 7
            "match_language",        // 8
            "match_modality",        // 9
            "match_tool",            // 10
            "p_pass_prior",          // 11 bo dem dat / truot theo cong cu (tien nghiem tu uy tin)
            "reputation",            // 12 0-1 (chua co = 0,5)
            "level",                 // 13
            "is_new",                // 14 chua du so nguoi thay
            "hours_to_deadline",     // 15 (khong han = 720)
            "hour_of_day",           // 16 gio Viet Nam 0-23
            "position",              // 17 vi tri the luc hien (luc xep = 0 — tranh thien vi vi tri)
            "requires_test",         // 18
            "project_start_rate",    // 19 bat dau / da thay cua du an
            "tried_before",          // 20 da tung lam loai du lieu / cong cu nay
        };

        public const int LogReward = 0;
        public const int Slots = 1;
        public const int Competitors = 2;
        public const int PSlot = 3;
        public const int EstMinutes = 4;
        public const int QuestionCount = 5;
        public const int Match = 6;
        public const int PPassPrior = 11;
        public const int IsNew = 14;
        public const int Position = 17;
        public const int ProjectStartRate = 19;

        public static int SoChieu
        {
            get { return Ten.Count; }
        }

        public static double[] Tinh(
            DuAnGoiY p,
            LabelerGoiY nguoi,
            ThongKeCho thongKe,
            DateTimeOffset luc,
            double chuKyQuenNgay,
            long nguongDuAnMoi)
        {
            if (p == null)
            {
                throw new ArgumentNullException(nameof(p));
            }

            if (nguoi == null)
            {
                throw new ArgumentNullException(nameof(nguoi));
            }

            if (thongKe == null)
            {
                throw new ArgumentNullException(nameof(thongKe));
            }

            SoThich st = nguoi.SoThich;
            double[] x = new double[SoChieu];

            double doiThuDat = p.DoiThu * ThongKeCho.TiLeDatChung;
            double pSlot = Math.Min(1.0, p.Slots / (1.0 + doiThuDat));
            double phut = Math.Max(0.5, p.QuestionCount * thongKe.GiayMoiCau(p) / 60.0);

            double mDomain = st.MucHopTrungBinh(LoaiThe.LinhVuc, p.Domains, luc, chuKyQuenNgay);
            double mLanguage = st.MucHopTrungBinh(LoaiThe.NgonNgu, p.Languages, luc, chuKyQuenNgay);
            double mModality = st.MucHop(LoaiThe.LoaiDuLieu, p.Modality, luc, chuKyQuenNgay);
            double mTool = st.MucHopTrungBinh(LoaiThe.CongCu, p.ToolKinds, luc, chuKyQuenNgay);

            // P(dat) truoc khi co mo hinh: bo dem dat / truot theo cong cu, tien nghiem Beta tu uy tin (chua co = 0,5).
            double uyTin = nguoi.Reputation.HasValue ? Math.Clamp(nguoi.Reputation.Value / 100.0, 0, 1) : 0.5;
            double pDat = PDatTruoc(st, p.ToolKinds, uyTin, luc, chuKyQuenNgay);

            double gioConLai = p.Deadline.HasValue ? Math.Max(0, (p.Deadline.Value - luc).TotalHours) : 720;
            int gioVn = luc.ToOffset(TimeSpan.FromHours(7)).Hour;

            x[0] = Math.Log(1 + p.RewardVnd / 1000.0);
            x[1] = p.Slots;
            x[2] = p.DoiThu;
            x[3] = pSlot;
            x[4] = phut;
            x[5] = p.QuestionCount;
            x[6] = (mDomain + mLanguage + mModality + mTool) / 4.0;
            x[7] = mDomain;
            x[8] = mLanguage;
            x[9] = mModality;
            x[10] = mTool;
            x[11] = pDat;
            x[12] = uyTin;
            x[13] = nguoi.Level;
            x[14] = p.SeenCount < nguongDuAnMoi ? 1 : 0;
            x[15] = Math.Min(gioConLai, 720);
            x[16] = gioVn;
            x[17] = 0;
            x[18] = p.RequireEntranceTest ? 1 : 0;
            x[19] = p.SeenCount > 0 ? Math.Min(1.0, (double)p.StartedCount / p.SeenCount) : 0;
            x[20] = st.DaTungLam(p) ? 1 : 0;
            return x;
        }

        private static double PDatTruoc(SoThich st, IReadOnlyList<string> congCu, double uyTin, DateTimeOffset luc, double chuKy)
        {
            // Tien nghiem Beta(1 + 2u, 1 + 2(1-u)): uy tin cao thi bat dau tu muc dat cao hon.
            double a0 = 1 + 2 * uyTin;
            double b0 = 1 + 2 * (1 - uyTin);
            if (congCu == null || congCu.Count == 0)
            {
                return a0 / (a0 + b0);
            }

            double tong = 0;
            foreach (string cc in congCu)
            {
                BoDem? d = st.Lay(LoaiThe.DatTheoCongCu, cc);
                double a = a0;
                double b = b0;
                if (d != null)
                {
                    BoDem q = d.QuenToi(luc, chuKy);
                    a += q.A;
                    b += q.B;
                }

                tong += a / (a + b);
            }

            return tong / congCu.Count;
        }
    }
}
