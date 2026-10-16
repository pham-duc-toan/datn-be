using System;
using System.Collections.Generic;
using System.Text;

namespace Crowd.Ledger.Domain.Invoices
{
    /// <summary>
    /// Doc so tien bang chu tieng Viet cho dong "So tien viet bang chu" tren hoa don GTGT.
    ///   50.000        → "Năm mươi nghìn đồng"
    ///   1.005.000     → "Một triệu không trăm linh năm nghìn đồng"
    ///   21.415.000    → "Hai mươi mốt triệu bốn trăm mười lăm nghìn đồng"
    /// Doc theo nhom 3 chu so; nhom khong dung dau co hang tram = 0 thi doc "không trăm".
    /// </summary>
    public static class DocSoTien
    {
        private static readonly string[] ChuSo = new string[]
        {
            "không", "một", "hai", "ba", "bốn", "năm", "sáu", "bảy", "tám", "chín",
        };

        private static readonly string[] DonViNhom = new string[] { string.Empty, "nghìn", "triệu" };

        public static string BangChu(long soTien)
        {
            if (soTien < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(soTien), "So tien am.");
            }

            if (soTien == 0)
            {
                return "Không đồng";
            }

            List<int> nhom = new List<int>();
            long con = soTien;
            while (con > 0)
            {
                nhom.Add((int)(con % 1000));
                con /= 1000;
            }

            List<string> phan = new List<string>();
            for (int i = nhom.Count - 1; i >= 0; i--)
            {
                if (nhom[i] == 0)
                {
                    continue;
                }

                bool laNhomDau = i == nhom.Count - 1;
                phan.Add(DocNhom(nhom[i], !laNhomDau));

                string donVi = TenDonVi(i);
                if (donVi.Length > 0)
                {
                    phan.Add(donVi);
                }
            }

            string kq = string.Join(" ", phan) + " đồng";
            return char.ToUpperInvariant(kq[0]) + kq.Substring(1);
        }

        /// <summary>Don vi cua nhom thu i (0 = hang don vi): "", nghìn, triệu, tỷ, nghìn tỷ, triệu tỷ, tỷ tỷ...</summary>
        private static string TenDonVi(int i)
        {
            StringBuilder sb = new StringBuilder(DonViNhom[i % 3]);
            for (int k = 0; k < i / 3; k++)
            {
                if (sb.Length > 0)
                {
                    sb.Append(' ');
                }

                sb.Append("tỷ");
            }

            return sb.ToString();
        }

        private static string DocNhom(int so, bool docDuTram)
        {
            int tram = so / 100;
            int chuc = (so / 10) % 10;
            int donVi = so % 10;
            List<string> t = new List<string>();

            if (tram > 0 || docDuTram)
            {
                t.Add(ChuSo[tram]);
                t.Add("trăm");
            }

            if (chuc == 0)
            {
                if (donVi > 0 && t.Count > 0)
                {
                    t.Add("linh");
                }
            }
            else if (chuc == 1)
            {
                t.Add("mười");
            }
            else
            {
                t.Add(ChuSo[chuc]);
                t.Add("mươi");
            }

            if (donVi > 0)
            {
                if (donVi == 1 && chuc > 1)
                {
                    t.Add("mốt");
                }
                else if (donVi == 5 && chuc > 0)
                {
                    t.Add("lăm");
                }
                else if (donVi == 4 && chuc > 1)
                {
                    t.Add("tư");
                }
                else
                {
                    t.Add(ChuSo[donVi]);
                }
            }

            return string.Join(" ", t);
        }
    }
}
