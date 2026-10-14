using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Crowd.Labeling.Tools
{
    /// <summary>Mot mat na da doc: kich thuoc + do dai cac doan 0 / 1 xen ke (bat dau bang doan 0).</summary>
    internal sealed class RleMask
    {
        public RleMask(int cao, int rong, long[] doan)
        {
            Cao = cao;
            Rong = rong;
            Doan = doan;
        }

        public int Cao { get; }

        public int Rong { get; }

        public long[] Doan { get; }
    }

    /// <summary>
    /// Mat na phan vung ma hoa RLE KHONG NEN kieu COCO:
    ///     {"size":[cao, rong], "counts":[so pixel 0, so pixel 1, so pixel 0, ...]}
    /// Pixel duyet theo COT (tu tren xuong, het cot thi sang cot ben phai) — pixel thu p nam o
    /// cot p / cao, hang p % cao. Doan dau tien luon la doan 0 (co the dai 0).
    ///
    /// Moi phep tinh di tren DOAN, khong bung ra tung pixel: mat na anh 4K van chi vai nghin doan.
    /// </summary>
    internal static class Rle
    {
        public static RleMask Doc(JsonObject phanTu)
        {
            JsonObject rle = (JsonObject)phanTu["rle"]!;
            JsonArray kichThuoc = (JsonArray)rle["size"]!;
            JsonArray mang = (JsonArray)rle["counts"]!;

            long[] doan = new long[mang.Count];
            for (int i = 0; i < mang.Count; i++)
            {
                doan[i] = HinhHoc.SoNguyenDai(mang[i], "nhan_khong_hop_le");
            }

            return new RleMask(HinhHoc.SoNguyen(kichThuoc[0]), HinhHoc.SoNguyen(kichThuoc[1]), doan);
        }

        public static JsonObject ToJson(int cao, int rong, IReadOnlyList<long> doan)
        {
            JsonArray mang = new JsonArray();
            foreach (long d in doan)
            {
                mang.Add(d);
            }

            return new JsonObject
            {
                ["size"] = new JsonArray(cao, rong),
                ["counts"] = mang,
            };
        }

        public static long TongPixel(long[] doan)
        {
            long tong = 0;
            foreach (long d in doan)
            {
                tong = tong + d;
            }

            return tong;
        }

        /// <summary>So pixel duoc bat (cac doan o vi tri le).</summary>
        public static long DienTich(long[] doan)
        {
            long tong = 0;
            for (int i = 1; i < doan.Length; i = i + 2)
            {
                tong = tong + doan[i];
            }

            return tong;
        }

        /// <summary>IoU hai mat na CUNG kich thuoc (khac kich thuoc → 0).</summary>
        public static double Iou(RleMask a, RleMask b)
        {
            if (a.Cao != b.Cao || a.Rong != b.Rong)
            {
                return 0;
            }

            // Loc nhanh: khoang [pixel bat dau tien, pixel bat cuoi cung] cua hai mat na khong cham nhau
            // (hai vung nam o hai dai cot khac nhau) thi IoU chac chan bang 0 — khoi di het cac doan.
            if (CuoiVung(a) <= DauVung(b) || CuoiVung(b) <= DauVung(a))
            {
                return 0;
            }

            long giao = 0;
            long hop = 0;
            int ia = 0;
            int ib = 0;
            long conA = a.Doan.Length > 0 ? a.Doan[0] : 0;
            long conB = b.Doan.Length > 0 ? b.Doan[0] : 0;

            while (ia < a.Doan.Length && ib < b.Doan.Length)
            {
                long buoc = Math.Min(conA, conB);
                bool batA = ia % 2 == 1;
                bool batB = ib % 2 == 1;

                if (batA && batB)
                {
                    giao = giao + buoc;
                }

                if (batA || batB)
                {
                    hop = hop + buoc;
                }

                conA = conA - buoc;
                conB = conB - buoc;

                if (conA == 0)
                {
                    ia++;
                    conA = ia < a.Doan.Length ? a.Doan[ia] : 0;
                }

                if (conB == 0)
                {
                    ib++;
                    conB = ib < b.Doan.Length ? b.Doan[ib] : 0;
                }
            }

            return hop == 0 ? 0 : (double)giao / hop;
        }

        /// <summary>Vi tri pixel duoc bat dau tien (= do dai doan 0 mo dau).</summary>
        private static long DauVung(RleMask m)
        {
            return m.Doan.Length > 0 ? m.Doan[0] : 0;
        }

        /// <summary>Vi tri ngay sau pixel duoc bat cuoi cung (so pixel tru doan 0 o duoi, neu co).</summary>
        private static long CuoiVung(RleMask m)
        {
            long tong = (long)m.Cao * m.Rong;
            return m.Doan.Length % 2 == 1 ? tong - m.Doan[m.Doan.Length - 1] : tong;
        }

        /// <summary>
        /// Mat na DA SO: pixel duoc bat khi HON MOT NUA so mat na bat no. Cac mat na phai cung
        /// tong so pixel. Tra ve day doan (bat dau bang doan 0).
        /// </summary>
        public static List<long> DaSo(IReadOnlyList<long[]> ds)
        {
            int k = ds.Count;
            int[] chiSo = new int[k];
            long[] con = new long[k];
            for (int i = 0; i < k; i++)
            {
                con[i] = ds[i].Length > 0 ? ds[i][0] : 0;
            }

            List<long> ra = new List<long>();
            bool dangBat = false;   // gia tri cua doan dang ghi (doan dau la doan 0)
            long dangDai = 0;

            while (true)
            {
                // Bo qua doan dai 0, va dung khi co mat na het doan.
                bool het = false;
                for (int i = 0; i < k; i++)
                {
                    while (con[i] == 0 && chiSo[i] < ds[i].Length)
                    {
                        chiSo[i]++;
                        con[i] = chiSo[i] < ds[i].Length ? ds[i][chiSo[i]] : 0;
                    }

                    if (chiSo[i] >= ds[i].Length)
                    {
                        het = true;
                    }
                }

                if (het)
                {
                    break;
                }

                long buoc = long.MaxValue;
                int soBat = 0;
                for (int i = 0; i < k; i++)
                {
                    buoc = Math.Min(buoc, con[i]);
                    if (chiSo[i] % 2 == 1)
                    {
                        soBat++;
                    }
                }

                bool bat = soBat * 2 > k;
                if (bat == dangBat)
                {
                    dangDai = dangDai + buoc;
                }
                else
                {
                    ra.Add(dangDai);
                    dangBat = bat;
                    dangDai = buoc;
                }

                for (int i = 0; i < k; i++)
                {
                    con[i] = con[i] - buoc;
                }
            }

            ra.Add(dangDai);
            return ra;
        }

        /// <summary>Hinh bao (x, y, w, h) cua vung duoc bat; null neu mat na rong.</summary>
        public static double[]? HopBao(RleMask m)
        {
            long minCot = long.MaxValue;
            long maxCot = long.MinValue;
            long minHang = long.MaxValue;
            long maxHang = long.MinValue;
            long viTri = 0;

            for (int i = 0; i < m.Doan.Length; i++)
            {
                long dai = m.Doan[i];
                if (i % 2 == 1 && dai > 0)
                {
                    long dau = viTri;
                    long cuoi = viTri + dai - 1;
                    long cotDau = dau / m.Cao;
                    long cotCuoi = cuoi / m.Cao;

                    minCot = Math.Min(minCot, cotDau);
                    maxCot = Math.Max(maxCot, cotCuoi);

                    if (cotDau == cotCuoi)
                    {
                        minHang = Math.Min(minHang, dau % m.Cao);
                        maxHang = Math.Max(maxHang, cuoi % m.Cao);
                    }
                    else
                    {
                        // Doan vat qua nhieu cot: cham day cot truoc va dinh cot sau.
                        minHang = 0;
                        maxHang = m.Cao - 1;
                    }
                }

                viTri = viTri + dai;
            }

            if (maxCot < minCot)
            {
                return null;
            }

            return new double[] { minCot, minHang, maxCot - minCot + 1, maxHang - minHang + 1 };
        }
    }
}
