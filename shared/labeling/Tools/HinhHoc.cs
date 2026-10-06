using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Crowd.Labeling.Tools
{
    /// <summary>Phep do dung chung cho so khop dap an.</summary>
    internal static class HinhHoc
    {
        /// <summary>Sai so cho phep khi so toa do voi kich thuoc mau (lam tron phia client).</summary>
        public const double SaiSo = 0.001;

        public static double So(JsonNode? n)
        {
            if (n == null)
            {
                throw new LabelFormatException("nhan_khong_hop_le", "Thieu so.");
            }

            return double.Parse(n.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        /// <summary>IoU hai hinh chu nhat (x, y, w, h): dien tich giao / dien tich hop.</summary>
        public static double IouHop(double x1, double y1, double w1, double h1, double x2, double y2, double w2, double h2)
        {
            double trai = Math.Max(x1, x2);
            double tren = Math.Max(y1, y2);
            double phai = Math.Min(x1 + w1, x2 + w2);
            double duoi = Math.Min(y1 + h1, y2 + h2);

            double giao = Math.Max(0, phai - trai) * Math.Max(0, duoi - tren);
            double hop = (w1 * h1) + (w2 * h2) - giao;
            return hop <= 0 ? 0 : giao / hop;
        }

        /// <summary>IoU hai doan [a1, b1] va [a2, b2] tren mot truc (thoi gian).</summary>
        public static double IouDoan(double a1, double b1, double a2, double b2)
        {
            double giao = Math.Max(0, Math.Min(b1, b2) - Math.Max(a1, a2));
            double hop = (b1 - a1) + (b2 - a2) - giao;
            return hop <= 0 ? 0 : giao / hop;
        }

        /// <summary>
        /// IoU hai da giac, UOC LUONG bang luoi diem: phu mot luoi 100 x 100 len
        /// hinh bao cua ca hai, dem diem nam trong tung da giac. Sai so ~1% —
        /// du cho cham cau vang (nguong thuong 0.5), doi lai don gian va dung voi
        /// ca da giac lom.
        /// </summary>
        public static double IouDaGiac(IReadOnlyList<double[]> p1, IReadOnlyList<double[]> p2)
        {
            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;

            foreach (IReadOnlyList<double[]> p in new IReadOnlyList<double[]>[] { p1, p2 })
            {
                foreach (double[] d in p)
                {
                    minX = Math.Min(minX, d[0]);
                    minY = Math.Min(minY, d[1]);
                    maxX = Math.Max(maxX, d[0]);
                    maxY = Math.Max(maxY, d[1]);
                }
            }

            const int N = 100;
            double buocX = (maxX - minX) / N;
            double buocY = (maxY - minY) / N;
            if (buocX <= 0 || buocY <= 0)
            {
                return 0;
            }

            int giao = 0;
            int hop = 0;
            for (int i = 0; i < N; i++)
            {
                for (int j = 0; j < N; j++)
                {
                    double x = minX + ((i + 0.5) * buocX);
                    double y = minY + ((j + 0.5) * buocY);
                    bool trong1 = TrongDaGiac(p1, x, y);
                    bool trong2 = TrongDaGiac(p2, x, y);

                    if (trong1 && trong2)
                    {
                        giao++;
                    }

                    if (trong1 || trong2)
                    {
                        hop++;
                    }
                }
            }

            return hop == 0 ? 0 : (double)giao / hop;
        }

        /// <summary>Ray casting: ban tia sang phai, cat canh le lan thi diem nam trong.</summary>
        public static bool TrongDaGiac(IReadOnlyList<double[]> p, double x, double y)
        {
            bool trong = false;
            for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
            {
                double xi = p[i][0];
                double yi = p[i][1];
                double xj = p[j][0];
                double yj = p[j][1];

                bool cat = (yi > y) != (yj > y) && x < ((xj - xi) * (y - yi) / (yj - yi)) + xi;
                if (cat)
                {
                    trong = !trong;
                }
            }

            return trong;
        }

        /// <summary>
        /// Ti le loi ky tu (CER): so phep sua (them / xoa / thay) it nhat de bien
        /// a thanh b, chia do dai b. Ca hai da chuan hoa: chu thuong, gop khoang trang.
        /// </summary>
        public static double Cer(string nop, string dapAn)
        {
            string a = ChuanHoaChu(nop);
            string b = ChuanHoaChu(dapAn);

            if (b.Length == 0)
            {
                return a.Length == 0 ? 0 : 1;
            }

            int[] truoc = new int[b.Length + 1];
            int[] nay = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++)
            {
                truoc[j] = j;
            }

            for (int i = 1; i <= a.Length; i++)
            {
                nay[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int thay = truoc[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                    nay[j] = Math.Min(Math.Min(truoc[j] + 1, nay[j - 1] + 1), thay);
                }

                int[] tam = truoc;
                truoc = nay;
                nay = tam;
            }

            return (double)truoc[b.Length] / b.Length;
        }

        public static string ChuanHoaChu(string s)
        {
            StringBuilder sb = new StringBuilder();
            bool vuaCach = false;

            foreach (char c in s.Trim().ToLowerInvariant())
            {
                if (char.IsWhiteSpace(c))
                {
                    if (!vuaCach)
                    {
                        sb.Append(' ');
                    }

                    vuaCach = true;
                }
                else
                {
                    sb.Append(c);
                    vuaCach = false;
                }
            }

            return sb.ToString();
        }
    }
}
