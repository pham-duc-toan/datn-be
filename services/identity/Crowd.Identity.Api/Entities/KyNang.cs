using System;
using System.Collections.Generic;
using Crowd.BuildingBlocks.Tags;
using Crowd.Identity.Api.Exceptions;

namespace Crowd.Identity.Api.Entities
{
    /// <summary>Kiem va chuan hoa danh sach ky nang cua labeler (FL-01).</summary>
    public static class KyNang
    {
        public const int SoMucToiDa = 20;
        public const int DoDaiToiDa = 50;

        /// <summary>Loai du lieu he thong ho tro — trung modality cua du an.</summary>
        public static readonly IReadOnlyList<string> LoaiDuLieu = new string[] { "image", "text", "audio", "video", "pair", "list" };

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
                    throw new InvalidValueException(maLoi, ten + ": mỗi mục từ 1 đến " + DoDaiToiDa + " ký tự.");
                }

                if (daCo.Add(v))
                {
                    kq.Add(v);
                }
            }

            if (kq.Count > SoMucToiDa)
            {
                throw new InvalidValueException(maLoi, ten + ": tối đa " + SoMucToiDa + " mục.");
            }

            return kq;
        }

        /// <summary>
        /// Linh vuc / ngon ngu la MA THE cua danh muc admin (MaThe): chu tu do cung duoc doi ve ma ("Y tế" → "y-te")
        /// de client cu van gui duoc. Bo trung, kiem so muc.
        /// </summary>
        public static List<string> ChuanHoaThe(IReadOnlyList<string>? ds, string maLoi, string ten)
        {
            List<string> kq = new List<string>();
            if (ds == null)
            {
                return kq;
            }

            foreach (string? muc in ds)
            {
                string ma = MaThe.TuChu(muc);
                if (ma.Length == 0)
                {
                    throw new InvalidValueException(maLoi, ten + ": “" + muc + "” không phải thẻ hợp lệ.");
                }

                if (!kq.Contains(ma))
                {
                    kq.Add(ma);
                }
            }

            if (kq.Count > SoMucToiDa)
            {
                throw new InvalidValueException(maLoi, ten + ": tối đa " + SoMucToiDa + " thẻ.");
            }

            return kq;
        }

        /// <summary>Gop the: thay maCu bang maMoi, bo trung. null = danh sach khong co maCu (khong doi).</summary>
        public static List<string>? ThayMa(IReadOnlyList<string> ds, string maCu, string maMoi)
        {
            if (!((ICollection<string>)ds).Contains(maCu))
            {
                return null;
            }

            List<string> kq = new List<string>();
            foreach (string ma in ds)
            {
                string v = ma == maCu ? maMoi : ma;
                if (!kq.Contains(v))
                {
                    kq.Add(v);
                }
            }

            return kq;
        }

        public static List<string> ChuanHoaLoaiDuLieu(IReadOnlyList<string>? ds)
        {
            List<string> kq = ChuanHoa(ds, "loai_du_lieu_khong_hop_le", "Loại dữ liệu");
            for (int i = 0; i < kq.Count; i++)
            {
                string thuong = kq[i].ToLowerInvariant();
                if (!((IList<string>)LoaiDuLieu).Contains(thuong))
                {
                    throw new InvalidValueException("loai_du_lieu_khong_hop_le", "Loại dữ liệu chỉ gồm: image, text, audio, video, pair.");
                }

                kq[i] = thuong;
            }

            return kq;
        }
    }
}
