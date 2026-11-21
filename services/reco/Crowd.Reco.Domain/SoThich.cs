using System;
using System.Collections.Generic;
using System.Globalization;

namespace Crowd.Reco.Domain
{
    /// <summary>Mot bo dem Beta(a, b) cua mot the — a: tin hieu thich / dat, b: tin hieu khong thich / truot.</summary>
    public sealed class BoDem
    {
        public BoDem(double a, double b, DateTimeOffset capNhatLuc)
        {
            if (a < 0 || b < 0 || double.IsNaN(a) || double.IsNaN(b))
            {
                throw new ArgumentOutOfRangeException(nameof(a), "Bo dem so thich khong duoc am.");
            }

            A = a;
            B = b;
            CapNhatLuc = capNhatLuc;
        }

        public double A { get; }

        public double B { get; }

        public DateTimeOffset CapNhatLuc { get; }

        /// <summary>He so quen dan: 0,5 ^ (so ngay / chu ky ban ra).</summary>
        public static double HeSoQuen(DateTimeOffset tu, DateTimeOffset den, double chuKyNgay)
        {
            if (chuKyNgay <= 0)
            {
                return 1;
            }

            double ngay = (den - tu).TotalDays;
            if (ngay <= 0)
            {
                return 1;
            }

            return Math.Pow(0.5, ngay / chuKyNgay);
        }

        /// <summary>Bo dem sau khi da quen toi luc <paramref name="luc"/>.</summary>
        public BoDem QuenToi(DateTimeOffset luc, double chuKyNgay)
        {
            double k = HeSoQuen(CapNhatLuc, luc, chuKyNgay);
            return new BoDem(A * k, B * k, luc > CapNhatLuc ? luc : CapNhatLuc);
        }

        /// <summary>Quen dan roi cong: a = a × 0,5^(ngay/chu ky) + wA (b tuong tu) — muc 4.2 ke hoach.</summary>
        public BoDem Cong(double wA, double wB, DateTimeOffset luc, double chuKyNgay)
        {
            BoDem quen = QuenToi(luc, chuKyNgay);
            return new BoDem(quen.A + wA, quen.B + wB, quen.CapNhatLuc);
        }

        /// <summary>Muc hop a/(a+b) voi tien nghiem Beta(1, 1) — the chua co tin hieu = 0,5.</summary>
        public double MucHop(DateTimeOffset luc, double chuKyNgay)
        {
            BoDem quen = QuenToi(luc, chuKyNgay);
            return (quen.A + 1) / (quen.A + quen.B + 2);
        }

        /// <summary>Dang luu trong Redis: "a,b,unixGiay".</summary>
        public string MaHoa()
        {
            return A.ToString("R", CultureInfo.InvariantCulture) + ","
                   + B.ToString("R", CultureInfo.InvariantCulture) + ","
                   + CapNhatLuc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        }

        public static BoDem? GiaiMa(string? s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return null;
            }

            string[] p = s.Split(',');
            double a;
            double b;
            long t;
            if (p.Length != 3
                || !double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out a)
                || !double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out b)
                || !long.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out t)
                || a < 0 || b < 0)
            {
                return null;
            }

            return new BoDem(a, b, DateTimeOffset.FromUnixTimeSeconds(t));
        }
    }

    /// <summary>
    /// So thich cua MOT labeler: bo dem theo (loai the, ma the). The chua cham toi khong co dong — coi nhu Beta(1, 1).
    /// Ban goc o reco_db.labeler_affinity, ban doc nhanh o Redis aff:{userId}.
    /// </summary>
    public sealed class SoThich
    {
        private readonly Dictionary<string, BoDem> _boDem;

        public SoThich()
        {
            _boDem = new Dictionary<string, BoDem>(StringComparer.Ordinal);
        }

        public SoThich(IDictionary<string, BoDem> boDem)
        {
            if (boDem == null)
            {
                throw new ArgumentNullException(nameof(boDem));
            }

            _boDem = new Dictionary<string, BoDem>(boDem, StringComparer.Ordinal);
        }

        public static string Khoa(string loai, string ma)
        {
            return loai + "|" + ma;
        }

        public IReadOnlyDictionary<string, BoDem> TatCa
        {
            get { return _boDem; }
        }

        public bool Co(string loai, string ma)
        {
            return _boDem.ContainsKey(Khoa(loai, ma));
        }

        public BoDem? Lay(string loai, string ma)
        {
            BoDem? d;
            return _boDem.TryGetValue(Khoa(loai, ma), out d) ? d : null;
        }

        public void Dat(string loai, string ma, BoDem d)
        {
            if (d == null)
            {
                throw new ArgumentNullException(nameof(d));
            }

            _boDem[Khoa(loai, ma)] = d;
        }

        /// <summary>Muc hop cua mot the (chua co = 0,5).</summary>
        public double MucHop(string loai, string ma, DateTimeOffset luc, double chuKyNgay)
        {
            BoDem? d = Lay(loai, ma);
            return d == null ? 0.5 : d.MucHop(luc, chuKyNgay);
        }

        /// <summary>Trung binh muc hop tren cac the cung loai cua du an (rong = 0,5).</summary>
        public double MucHopTrungBinh(string loai, IReadOnlyList<string> ma, DateTimeOffset luc, double chuKyNgay)
        {
            if (ma == null || ma.Count == 0)
            {
                return 0.5;
            }

            double tong = 0;
            foreach (string m in ma)
            {
                tong += MucHop(loai, m, luc, chuKyNgay);
            }

            return tong / ma.Count;
        }

        /// <summary>Labeler da tung co tin hieu voi loai du lieu / cong cu nay chua (de chon the "kham pha").</summary>
        public bool DaTungLam(DuAnGoiY duAn)
        {
            if (duAn == null)
            {
                throw new ArgumentNullException(nameof(duAn));
            }

            if (Co(LoaiThe.LoaiDuLieu, duAn.Modality))
            {
                return true;
            }

            foreach (string t in duAn.ToolKinds)
            {
                if (Co(LoaiThe.CongCu, t))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
