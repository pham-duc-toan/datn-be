using System;
using System.Collections.Generic;
using System.Globalization;

namespace Crowd.Identity.Api.Services
{
    /// <summary>
    /// Luat cap do va huy hieu cua labeler (FL-12) — ham thuan, test duoc.
    ///
    /// Cap: dat DONG THOI so nhan duoc duyet va uy tin toi thieu cua cap do (setting
    /// identity.level_rules, vd "2=50/50;3=200/65"). Cap 1 la mac dinh. Uy tin giam thi cap co the
    /// giam theo — cap la trang thai hien tai, khong phai thanh tich.
    /// Huy hieu: thanh tich, dat roi thi giu.
    /// </summary>
    public static class CapDo
    {
        public const string LuatMacDinh = "2=50/50;3=200/65;4=1000/75;5=5000/85";

        public static readonly IReadOnlyList<HuyHieu> DanhMucHuyHieu = new HuyHieu[]
        {
            new HuyHieu("moc_100", "100 nhãn được duyệt", 100, null),
            new HuyHieu("moc_1000", "1.000 nhãn được duyệt", 1000, null),
            new HuyHieu("moc_10000", "10.000 nhãn được duyệt", 10000, null),
            new HuyHieu("uy_tin_cao", "Uy tín từ 90 trở lên", 100, 90),
        };

        /// <summary>Doc luat tu setting. Sai dinh dang → null (noi goi dung LuatMacDinh).</summary>
        public static IReadOnlyList<DieuKienCap>? DocLuat(string? luat)
        {
            if (string.IsNullOrWhiteSpace(luat))
            {
                return null;
            }

            List<DieuKienCap> ds = new List<DieuKienCap>();
            foreach (string phan in luat.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] capVaDieuKien = phan.Split('=');
                if (capVaDieuKien.Length != 2)
                {
                    return null;
                }

                string[] nhanVaUyTin = capVaDieuKien[1].Split('/');
                int cap;
                int soNhan;
                int uyTin;
                if (nhanVaUyTin.Length != 2
                    || !int.TryParse(capVaDieuKien[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out cap)
                    || !int.TryParse(nhanVaUyTin[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out soNhan)
                    || !int.TryParse(nhanVaUyTin[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out uyTin)
                    || cap < 2 || uyTin > 100)
                {
                    return null;
                }

                ds.Add(new DieuKienCap(cap, soNhan, uyTin));
            }

            return ds.Count == 0 ? null : ds;
        }

        /// <summary>Cap cao nhat ma ca so nhan va uy tin deu dat. Chua co uy tin → cap 1.</summary>
        public static int TinhCap(IReadOnlyList<DieuKienCap> luat, int soNhanDuyet, int? uyTin)
        {
            if (luat == null)
            {
                throw new ArgumentNullException(nameof(luat));
            }

            int cap = 1;
            if (!uyTin.HasValue)
            {
                return cap;
            }

            foreach (DieuKienCap d in luat)
            {
                if (d.Cap > cap && soNhanDuyet >= d.SoNhanToiThieu && uyTin.Value >= d.UyTinToiThieu)
                {
                    cap = d.Cap;
                }
            }

            return cap;
        }

        /// <summary>Huy hieu vua du dieu kien ma chua co trong <paramref name="daCo"/>.</summary>
        public static List<HuyHieu> HuyHieuMoi(int soNhanDuyet, int? uyTin, ISet<string> daCo)
        {
            if (daCo == null)
            {
                throw new ArgumentNullException(nameof(daCo));
            }

            List<HuyHieu> moi = new List<HuyHieu>();
            foreach (HuyHieu h in DanhMucHuyHieu)
            {
                bool duNhan = soNhanDuyet >= h.SoNhanToiThieu;
                bool duUyTin = !h.UyTinToiThieu.HasValue || (uyTin.HasValue && uyTin.Value >= h.UyTinToiThieu.Value);
                if (duNhan && duUyTin && !daCo.Contains(h.Ma))
                {
                    moi.Add(h);
                }
            }

            return moi;
        }

        public static HuyHieu? Tim(string ma)
        {
            foreach (HuyHieu h in DanhMucHuyHieu)
            {
                if (string.Equals(h.Ma, ma, StringComparison.Ordinal))
                {
                    return h;
                }
            }

            return null;
        }
    }

    public sealed class DieuKienCap
    {
        public DieuKienCap(int cap, int soNhanToiThieu, int uyTinToiThieu)
        {
            Cap = cap;
            SoNhanToiThieu = soNhanToiThieu;
            UyTinToiThieu = uyTinToiThieu;
        }

        public int Cap { get; }

        public int SoNhanToiThieu { get; }

        public int UyTinToiThieu { get; }
    }

    public sealed class HuyHieu
    {
        public HuyHieu(string ma, string ten, int soNhanToiThieu, int? uyTinToiThieu)
        {
            Ma = ma;
            Ten = ten;
            SoNhanToiThieu = soNhanToiThieu;
            UyTinToiThieu = uyTinToiThieu;
        }

        public string Ma { get; }

        public string Ten { get; }

        public int SoNhanToiThieu { get; }

        public int? UyTinToiThieu { get; }
    }
}
