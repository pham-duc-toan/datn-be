using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Crowd.Labeling.Tools
{
    /// <summary>
    /// Phep do dung chung cho so khop dap an va gop nhan nhieu nguoi.
    ///
    /// quality-svc (Python, app/gop.py) cai LAI dung cac cong thuc nay — hai ben doi chieu bang
    /// bo vi du chung contracts/labeling/testdata/gop.vectors.json. Sua cong thuc o day thi sua
    /// ca ben kia va cap nhat bo vi du.
    /// </summary>
    internal static class HinhHoc
    {
        /// <summary>Sai so cho phep khi so toa do voi kich thuoc mau (lam tron phia client).</summary>
        public const double SaiSo = 0.001;

        /// <summary>Sai so khi so mot do giong / diem voi nguong (tranh truot vi lam tron so thuc).</summary>
        public const double SaiSoNguong = 1e-9;

        /// <summary>So diem lay mau tren moi duong gap khuc khi do do lech.</summary>
        public const int SoDiemMauDuong = 32;

        /// <summary>So thoi diem lay mau khi do IoU cua hai chuoi khung (track).</summary>
        public const int SoMocMauTrack = 50;

        public static double So(JsonNode? n)
        {
            if (n == null)
            {
                throw new LabelFormatException("nhan_khong_hop_le", "Thiếu giá trị số.");
            }

            // Duong nhanh: doc thang so trong JsonValue (khong viet ra chuoi roi doc lai) — cung ket qua.
            JsonValue? v = n as JsonValue;
            double d;
            if (v != null && v.TryGetValue(out d))
            {
                return d;
            }

            return double.Parse(n.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Doc mot SO NGUYEN tu JSON. JSON Schema coi 5.0 va 1e2 la "integer", con GetValue&lt;int&gt;() cua
        /// System.Text.Json thi nem loi voi chung (va voi so vuot kieu int) — thanh 500 thay vi 400. Doc qua
        /// so thuc roi kiem: nguyen va trong gioi han thi nhan, khong thi bao loi dinh dang.
        /// </summary>
        public static long SoNguyenDai(JsonNode? n, string maLoi)
        {
            double v = So(n);
            if (double.IsNaN(v) || v != Math.Floor(v) || Math.Abs(v) > 9007199254740992.0)
            {
                throw new LabelFormatException(maLoi, "Giá trị " + (n == null ? "trống" : n.ToJsonString()) + " không phải số nguyên hợp lệ.");
            }

            return (long)v;
        }

        public static int SoNguyen(JsonNode? n, string maLoi)
        {
            long v = SoNguyenDai(n, maLoi);
            if (v > int.MaxValue || v < int.MinValue)
            {
                throw new LabelFormatException(maLoi, "Giá trị " + n!.ToJsonString() + " quá lớn.");
            }

            return (int)v;
        }

        /// <summary>So nguyen trong NHAN (sai → "nhan_khong_hop_le").</summary>
        public static int SoNguyen(JsonNode? n)
        {
            return SoNguyen(n, "nhan_khong_hop_le");
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
        /// hinh bao cua ca hai, dem diem nam trong tung da giac (ray casting: ban tia
        /// sang phai, cat canh le lan thi diem nam trong). Sai so ~1% — du cho cham cau
        /// vang va ghep cum (nguong thuong 0.5), doi lai don gian va dung voi ca da giac lom.
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

            // Quet tung HANG cua luoi: tinh mot lan cac giao diem cua hang voi canh da giac, roi di doc
            // hang — diem nam trong khi so giao diem ben PHAI no la le (ray casting). Cung phep so sanh
            // voi viec ban tia cho tung diem, nhung moi canh chi tinh mot lan moi hang thay vi moi diem.
            double[] x1 = ToaDo(p1, 0);
            double[] y1 = ToaDo(p1, 1);
            double[] x2 = ToaDo(p2, 0);
            double[] y2 = ToaDo(p2, 1);
            double[] cat1 = new double[x1.Length];
            double[] cat2 = new double[x2.Length];

            int giao = 0;
            int hop = 0;
            for (int j = 0; j < N; j++)
            {
                double y = minY + ((j + 0.5) * buocY);
                int soCat1 = GiaoDiemVoiHang(x1, y1, y, cat1);
                int soCat2 = GiaoDiemVoiHang(x2, y2, y, cat2);
                int qua1 = 0;   // so giao diem KHONG nam ben phai diem dang xet
                int qua2 = 0;

                for (int i = 0; i < N; i++)
                {
                    double x = minX + ((i + 0.5) * buocX);

                    while (qua1 < soCat1 && !(x < cat1[qua1]))
                    {
                        qua1++;
                    }

                    while (qua2 < soCat2 && !(x < cat2[qua2]))
                    {
                        qua2++;
                    }

                    bool trong1 = (soCat1 - qua1) % 2 == 1;
                    bool trong2 = (soCat2 - qua2) % 2 == 1;

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

        private static double[] ToaDo(IReadOnlyList<double[]> p, int truc)
        {
            double[] ra = new double[p.Count];
            for (int i = 0; i < p.Count; i++)
            {
                ra[i] = p[i][truc];
            }

            return ra;
        }

        /// <summary>Ghi hoanh do cac giao diem cua duong ngang y voi canh da giac vao ra[0..n), tang dan; tra ve n.</summary>
        private static int GiaoDiemVoiHang(double[] xs, double[] ys, double y, double[] ra)
        {
            int n = 0;
            for (int i = 0, j = xs.Length - 1; i < xs.Length; j = i++)
            {
                double xi = xs[i];
                double yi = ys[i];
                double xj = xs[j];
                double yj = ys[j];

                if ((yi > y) != (yj > y))
                {
                    ra[n] = ((xj - xi) * (y - yi) / (yj - yi)) + xi;
                    n++;
                }
            }

            Array.Sort(ra, 0, n);
            return n;
        }

        /// <summary>Hinh bao cua hai tap diem co cham / de len nhau khong (loc nhanh truoc khi tinh IoU).</summary>
        public static bool HinhBaoGiaoNhau(IReadOnlyList<double[]> p1, IReadOnlyList<double[]> p2)
        {
            double[] a = HinhBao(p1);
            double[] b = HinhBao(p2);
            return !(a[2] < b[0] || b[2] < a[0] || a[3] < b[1] || b[3] < a[1]);
        }

        /// <summary>[minX, minY, maxX, maxY] cua mot tap diem.</summary>
        public static double[] HinhBao(IReadOnlyList<double[]> p)
        {
            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;

            foreach (double[] d in p)
            {
                minX = Math.Min(minX, d[0]);
                minY = Math.Min(minY, d[1]);
                maxX = Math.Max(maxX, d[0]);
                maxY = Math.Max(maxY, d[1]);
            }

            return new double[] { minX, minY, maxX, maxY };
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

            return (double)KhoangCachSua(a, b) / b.Length;
        }

        /// <summary>
        /// Do lech DOI XUNG giua hai van ban (0 = giong het, 1 = khac hoan toan): khoang cach sua
        /// chia do dai ban dai hon, sau khi chuan hoa. Dung chon ban "o giua" khi gop nhieu ban chep.
        /// </summary>
        public static double DoLechChu(string x, string y)
        {
            string a = ChuanHoaChu(x);
            string b = ChuanHoaChu(y);
            int dai = Math.Max(a.Length, b.Length);
            return dai == 0 ? 0 : (double)KhoangCachSua(a, b) / dai;
        }

        /// <summary>Khoang cach Levenshtein: so phep them / xoa / thay it nhat de bien a thanh b.</summary>
        public static int KhoangCachSua(string a, string b)
        {
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

            return truoc[b.Length];
        }

        // =====================================================================
        // SO: lam tron, trung vi
        // =====================================================================

        /// <summary>Lam tron 3 chu so thap phan (nua thi len) — cung cong thuc voi ben Python.</summary>
        public static double LamTron(double v)
        {
            return Math.Floor((v * 1000) + 0.5) / 1000;
        }

        /// <summary>Trung vi; so phan tu chan thi lay trung binh hai phan tu giua.</summary>
        public static double TrungVi(IEnumerable<double> giaTri)
        {
            List<double> ds = new List<double>(giaTri);
            ds.Sort();
            if (ds.Count == 0)
            {
                throw new ArgumentException("Can it nhat mot gia tri.", nameof(giaTri));
            }

            int giua = ds.Count / 2;
            return ds.Count % 2 == 1 ? ds[giua] : (ds[giua - 1] + ds[giua]) / 2;
        }

        // =====================================================================
        // DIEM va DUONG (keypoint, polyline)
        // =====================================================================

        public static double KhoangCach(double x1, double y1, double x2, double y2)
        {
            double dx = x1 - x2;
            double dy = y1 - y2;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        /// <summary>
        /// Thuoc do de chuan hoa do lech cua diem / duong: DUONG CHEO ANH. Mau khong co kich thuoc
        /// (anh cu chua do) thi dung duong cheo hinh bao cua cac diem dang xet (toi thieu 1).
        /// </summary>
        public static double DuongCheo(SampleMetadata mau, IEnumerable<double[]> diemDuPhong)
        {
            if (mau.Width.HasValue && mau.Height.HasValue && mau.Width.Value > 0 && mau.Height.Value > 0)
            {
                return KhoangCach(0, 0, mau.Width.Value, mau.Height.Value);
            }

            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;
            bool co = false;

            foreach (double[] d in diemDuPhong)
            {
                co = true;
                minX = Math.Min(minX, d[0]);
                minY = Math.Min(minY, d[1]);
                maxX = Math.Max(maxX, d[0]);
                maxY = Math.Max(maxY, d[1]);
            }

            if (!co)
            {
                return 1;
            }

            return Math.Max(1, KhoangCach(minX, minY, maxX, maxY));
        }

        /// <summary>Do dai duong gap khuc.</summary>
        public static double DoDaiDuong(IReadOnlyList<double[]> p)
        {
            double tong = 0;
            for (int i = 1; i < p.Count; i++)
            {
                tong = tong + KhoangCach(p[i - 1][0], p[i - 1][1], p[i][0], p[i][1]);
            }

            return tong;
        }

        /// <summary>Lay soDiem diem cach deu theo DO DAI doc duong gap khuc (ca hai dau mut).</summary>
        public static List<double[]> LayMauDuong(IReadOnlyList<double[]> p, int soDiem)
        {
            List<double[]> ra = new List<double[]>(soDiem);
            double tong = DoDaiDuong(p);

            if (tong <= 0)
            {
                for (int k = 0; k < soDiem; k++)
                {
                    ra.Add(new double[] { p[0][0], p[0][1] });
                }

                return ra;
            }

            int doan = 1;            // dang o doan p[doan-1] → p[doan]
            double daDi = 0;         // do dai tinh toi p[doan-1]
            double daiDoan = KhoangCach(p[0][0], p[0][1], p[1][0], p[1][1]);

            for (int k = 0; k < soDiem; k++)
            {
                double dich = tong * k / (soDiem - 1);

                while (doan < p.Count - 1 && daDi + daiDoan < dich)
                {
                    daDi = daDi + daiDoan;
                    doan++;
                    daiDoan = KhoangCach(p[doan - 1][0], p[doan - 1][1], p[doan][0], p[doan][1]);
                }

                double t = daiDoan <= 0 ? 0 : (dich - daDi) / daiDoan;
                if (t < 0)
                {
                    t = 0;
                }

                if (t > 1)
                {
                    t = 1;
                }

                ra.Add(new double[]
                {
                    p[doan - 1][0] + (t * (p[doan][0] - p[doan - 1][0])),
                    p[doan - 1][1] + (t * (p[doan][1] - p[doan - 1][1])),
                });
            }

            return ra;
        }

        /// <summary>
        /// Do lech trung binh (pixel) giua hai duong gap khuc: lay mau deu tren moi duong, trung binh
        /// khoang cach tung cap diem tuong ung; thu ca chieu nguoc (ve tu dau nao cung la mot duong).
        /// </summary>
        public static double DoLechDuong(IReadOnlyList<double[]> p1, IReadOnlyList<double[]> p2)
        {
            return DoLechMauDuong(LayMauDuong(p1, SoDiemMauDuong), LayMauDuong(p2, SoDiemMauDuong));
        }

        /// <summary>DoLechDuong tren hai day diem DA lay mau (moi day SoDiemMauDuong diem).</summary>
        public static double DoLechMauDuong(IReadOnlyList<double[]> a, IReadOnlyList<double[]> b)
        {
            double xuoi = 0;
            double nguoc = 0;
            for (int k = 0; k < SoDiemMauDuong; k++)
            {
                double[] q = b[k];
                double[] r = b[SoDiemMauDuong - 1 - k];
                xuoi = xuoi + KhoangCach(a[k][0], a[k][1], q[0], q[1]);
                nguoc = nguoc + KhoangCach(a[k][0], a[k][1], r[0], r[1]);
            }

            return Math.Min(xuoi, nguoc) / SoDiemMauDuong;
        }

        // =====================================================================
        // XEP HANG (ranking)
        // =====================================================================

        /// <summary>
        /// Do giong hai thu tu xep hang cua CUNG tap muc: ti le cap muc duoc xep cung chieu
        /// (Kendall), 0..1. Hai thu tu khac tap muc → 0.
        /// </summary>
        public static double Kendall(IReadOnlyList<int> a, IReadOnlyList<int> b)
        {
            if (a.Count != b.Count)
            {
                return 0;
            }

            Dictionary<int, int> viTri = new Dictionary<int, int>();
            for (int i = 0; i < b.Count; i++)
            {
                viTri[b[i]] = i;
            }

            foreach (int muc in a)
            {
                if (!viTri.ContainsKey(muc))
                {
                    return 0;
                }
            }

            int tong = 0;
            int cungChieu = 0;
            for (int i = 0; i < a.Count; i++)
            {
                for (int j = i + 1; j < a.Count; j++)
                {
                    tong++;
                    if (viTri[a[i]] < viTri[a[j]])
                    {
                        cungChieu++;
                    }
                }
            }

            return tong == 0 ? 1 : (double)cungChieu / tong;
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
