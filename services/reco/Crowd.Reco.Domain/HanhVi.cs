using System;
using System.Collections.Generic;

namespace Crowd.Reco.Domain
{
    /// <summary>Mot thay doi bo dem: cong wA vao a, wB vao b cua the (loai, ma).</summary>
    public sealed class ThayDoiBoDem
    {
        public required string Loai { get; init; }

        public required string Ma { get; init; }

        public required double WA { get; init; }

        public required double WB { get; init; }
    }

    /// <summary>
    /// Hanh vi → cong bao nhieu vao bo dem so thich (bang muc 4.2 ke hoach). Bo dem luu so THO; muc hop tinh voi tien
    /// nghiem Beta(1, 1), nen "the chon luc dang ky" = a 2, b 0 → muc hop 0,75 (tuong duong a = 3, b = 1 trong ke hoach).
    /// </summary>
    public static class HanhVi
    {
        public const string DangKy = "register";
        public const string HienThe = "impression";
        public const string MoThe = "open";
        public const string KhongQuanTam = "not_interested";
        public const string BatDau = "start";
        public const string Nop = "submit";
        public const string Dat = "pass";
        public const string Truot = "fail";
        public const string BoDo = "abandon";

        /// <summary>Bam tim — luu de lam sau (3.40). Tin hieu quan tam ro: cong a + 0,3; ml-svc tinh nhu nhan "bat dau".</summary>
        public const string Thich = "like";

        /// <summary>Bo tim: b + 0,3 (nhe hon "khong quan tam").</summary>
        public const string BoThich = "unlike";

        /// <summary>Hanh vi XOA phat "da luot qua" cua du an (nguoi dung da quan tam that).</summary>
        public static readonly IReadOnlyList<string> XoaLuotQua = new string[] { "open", "like", "start", "submit" };

        /// <summary>Hanh vi frontend duoc phep gui qua POST /reco/events.</summary>
        public static readonly IReadOnlyList<string> TuFrontend = new string[] { HienThe, MoThe, KhongQuanTam };

        /// <summary>Thay doi bo dem cho MOT hanh vi tren MOT du an (rong = hanh vi khong doi so thich, vd mo the).</summary>
        public static List<ThayDoiBoDem> ThayDoi(string hanhVi, DuAnGoiY duAn)
        {
            if (duAn == null)
            {
                throw new ArgumentNullException(nameof(duAn));
            }

            double wA = 0;
            double wB = 0;
            bool congDat = false;
            switch (hanhVi)
            {
                case HienThe:
                    wB = 0.05;
                    break;
                case KhongQuanTam:
                    wB = 1;
                    break;
                case BatDau:
                    wA = 0.2;
                    break;
                case Nop:
                    wA = 0.5;
                    break;
                case Dat:
                    wA = 1;
                    congDat = true;
                    break;
                case Truot:
                    wB = 1;
                    congDat = true;
                    break;
                case BoDo:
                    wB = 0.5;
                    break;
                case Thich:
                    wA = 0.3;
                    break;
                case BoThich:
                    wB = 0.3;
                    break;
                default:
                    return new List<ThayDoiBoDem>();
            }

            List<ThayDoiBoDem> ds = new List<ThayDoiBoDem>();
            foreach (KeyValuePair<string, string> the in duAn.CacThe())
            {
                ds.Add(new ThayDoiBoDem { Loai = the.Key, Ma = the.Value, WA = wA, WB = wB });
            }

            if (congDat)
            {
                foreach (string cc in duAn.ToolKinds)
                {
                    ds.Add(new ThayDoiBoDem
                    {
                        Loai = LoaiThe.DatTheoCongCu,
                        Ma = cc,
                        WA = hanhVi == Dat ? 1 : 0,
                        WB = hanhVi == Truot ? 1 : 0,
                    });
                }
            }

            return ds;
        }

        /// <summary>So khoi dau tu the chon luc dang ky (user.registered v2).</summary>
        public static List<ThayDoiBoDem> TuDangKy(IReadOnlyList<string>? linhVuc, IReadOnlyList<string>? ngonNgu, IReadOnlyList<string>? loaiDuLieu)
        {
            List<ThayDoiBoDem> ds = new List<ThayDoiBoDem>();
            Them(ds, LoaiThe.LinhVuc, linhVuc);
            Them(ds, LoaiThe.NgonNgu, ngonNgu);
            Them(ds, LoaiThe.LoaiDuLieu, loaiDuLieu);
            return ds;
        }

        private static void Them(List<ThayDoiBoDem> ds, string loai, IReadOnlyList<string>? ma)
        {
            if (ma == null)
            {
                return;
            }

            foreach (string m in ma)
            {
                if (!string.IsNullOrWhiteSpace(m))
                {
                    ds.Add(new ThayDoiBoDem { Loai = loai, Ma = m.Trim(), WA = 2, WB = 0 });
                }
            }
        }
    }
}
