using System;
using System.Globalization;
using System.Text;

namespace Crowd.BuildingBlocks.Tags
{
    /// <summary>
    /// Ma the chu de (linh vuc / ngon ngu) dung chung: admin-svc giu danh muc (ten hien thi, an, gop), identity-svc
    /// (ho so ky nang) va project-svc (nhan du an) chi luu MA — doi ten the khong dong vao du lieu da gan, goi y du an
    /// so khop theo ma. Ma = chu thuong khong dau a-z 0-9, noi bang '-' (vd "y-te", "vi").
    /// </summary>
    public static class MaThe
    {
        public const int DoDaiToiDa = 50;

        /// <summary>Loai the: linh vuc (y te, giao thong...).</summary>
        public const string LinhVuc = "domain";

        /// <summary>Loai the: ngon ngu (ma ISO 639-1: vi, en...).</summary>
        public const string NgonNgu = "language";

        public static bool LaLoaiHopLe(string? loai)
        {
            return loai == LinhVuc || loai == NgonNgu;
        }

        /// <summary>
        /// Chuan hoa ve ma: bo dau tieng Viet, chu thuong, moi cum ky tu khac a-z 0-9 thanh mot '-'. "Y tế" → "y-te".
        /// Chuoi rong / chi co ky tu dac biet → "".
        /// </summary>
        public static string TuChu(string? chu)
        {
            if (string.IsNullOrWhiteSpace(chu))
            {
                return string.Empty;
            }

            string tach = chu.Trim().ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD);
            StringBuilder sb = new StringBuilder(tach.Length);
            bool vuaGach = false;
            foreach (char c in tach)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    sb.Append(c);
                    vuaGach = false;
                }
                else if (!vuaGach && sb.Length > 0)
                {
                    sb.Append('-');
                    vuaGach = true;
                }
            }

            string ma = sb.ToString().TrimEnd('-');
            return ma.Length > DoDaiToiDa ? ma.Substring(0, DoDaiToiDa).TrimEnd('-') : ma;
        }
    }
}
