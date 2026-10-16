using System;
using System.Collections.Generic;
using System.Linq;

namespace Crowd.Tasking.Domain.Assignments
{
    /// <summary>Mot khoang thoi gian labeler lam mot task (tu luc nhan toi luc nop / bo qua / het han).</summary>
    public sealed class KhoangLamViec
    {
        public KhoangLamViec(DateTimeOffset batDau, DateTimeOffset ketThuc)
        {
            BatDau = batDau;
            KetThuc = ketThuc < batDau ? batDau : ketThuc;
        }

        public DateTimeOffset BatDau { get; }

        public DateTimeOffset KetThuc { get; }
    }

    /// <summary>
    /// GIOI HAN PHOI NHIEM noi dung nhay cam (muc 2.9): lam LIEN TUC toi da "toiDa" thi phai nghi it nhat
    /// "nghi". Hai task cach nhau it hon "nghi" coi nhu cung mot phien (luot xem lien tiep). Tinh tren
    /// moi du an nhay cam gop lai — doi sang du an nhay cam khac khong "reset" duoc dong ho.
    ///
    /// Ham thuan (khong doc database, khong doc dong ho) de unit test duoc moi bien.
    /// </summary>
    public static class PhienLamViec
    {
        /// <summary>
        /// null = duoc nhan task moi; co gia tri = phai nghi toi luc do.
        /// </summary>
        /// <param name="khoang">Cac luot lam du an nhay cam gan day (thu tu bat ky).</param>
        public static DateTimeOffset? PhaiNghiDen(IEnumerable<KhoangLamViec> khoang, DateTimeOffset bayGio, TimeSpan toiDa, TimeSpan nghi)
        {
            if (khoang == null)
            {
                throw new ArgumentNullException(nameof(khoang));
            }

            List<KhoangLamViec> ds = khoang.OrderBy(k => k.BatDau).ToList();
            if (ds.Count == 0)
            {
                return null;
            }

            // Ket thuc muon nhat cua cac luot (luot dang giu: ket thuc = bay gio).
            DateTimeOffset cuoi = ds.Max(k => k.KetThuc);
            if (bayGio - cuoi >= nghi)
            {
                return null;
            }

            // Lan nguoc tu luot moi nhat: gap giua hai luot ngan hon "nghi" thi van cung phien.
            DateTimeOffset dauPhien = ds[ds.Count - 1].BatDau;
            DateTimeOffset ketTruoc = ds[ds.Count - 1].BatDau;
            for (int i = ds.Count - 2; i >= 0; i--)
            {
                if (ketTruoc - ds[i].KetThuc >= nghi)
                {
                    break;
                }

                dauPhien = ds[i].BatDau < dauPhien ? ds[i].BatDau : dauPhien;
                ketTruoc = ds[i].BatDau;
            }

            if (bayGio - dauPhien >= toiDa)
            {
                return cuoi + nghi;
            }

            return null;
        }
    }
}
