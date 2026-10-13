using System;
using System.Collections.Generic;
using Crowd.Identity.Api.Exceptions;

namespace Crowd.Identity.Api.Entities
{
    /// <summary>Kiem va chuan hoa danh sach ky nang cua labeler (FL-01).</summary>
    public static class KyNang
    {
        public const int SoMucToiDa = 20;
        public const int DoDaiToiDa = 50;

        /// <summary>Loai du lieu he thong ho tro — trung modality cua du an.</summary>
        public static readonly IReadOnlyList<string> LoaiDuLieu = new string[] { "image", "text", "audio", "video", "pair" };

        /// <summary>Cat khoang trang, bo trung (khong phan biet hoa thuong), kiem so muc va do dai.</summary>
        public static List<string> ChuanHoa(IReadOnlyList<string>? ds, string maLoi, string ten)
        {
            List<string> kq = new List<string>();
            if (ds == null)
            {
                return kq;
            }

            HashSet<string> daCo = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string? muc in ds)
            {
                string v = (muc ?? string.Empty).Trim();
                if (v.Length == 0 || v.Length > DoDaiToiDa)
                {
                    throw new InvalidValueException(maLoi, ten + ": moi muc tu 1 den " + DoDaiToiDa + " ky tu.");
                }

                if (daCo.Add(v))
                {
                    kq.Add(v);
                }
            }

            if (kq.Count > SoMucToiDa)
            {
                throw new InvalidValueException(maLoi, ten + ": toi da " + SoMucToiDa + " muc.");
            }

            return kq;
        }

        public static List<string> ChuanHoaLoaiDuLieu(IReadOnlyList<string>? ds)
        {
            List<string> kq = ChuanHoa(ds, "loai_du_lieu_khong_hop_le", "Loai du lieu");
            for (int i = 0; i < kq.Count; i++)
            {
                string thuong = kq[i].ToLowerInvariant();
                if (!((IList<string>)LoaiDuLieu).Contains(thuong))
                {
                    throw new InvalidValueException("loai_du_lieu_khong_hop_le", "Loai du lieu chi gom: image, text, audio, video, pair.");
                }

                kq[i] = thuong;
            }

            return kq;
        }
    }
}
