using System;

namespace Crowd.Project.Infrastructure.Media
{
    /// <summary>
    /// Doc KICH THUOC anh tu phan dau file, khong giai ma ca anh — can de kiem
    /// khung (bbox / polygon) co nam trong anh khong.
    ///
    ///   PNG : khoi IHDR ngay sau chu ky 8 byte — rong, cao (4 byte big-endian moi so).
    ///   JPEG: duyet cac doan (marker 0xFF..) toi doan SOF (0xC0..0xCF tru C4, C8, CC).
    ///   WebP: "RIFF....WEBP" roi VP8 / VP8L / VP8X, moi loai ghi kich thuoc mot kieu.
    ///
    /// Tra ve null neu khong doc duoc (file hong / dinh dang khac).
    /// </summary>
    public static class ImageHeader
    {
        public static (int Width, int Height)? DocKichThuoc(byte[] b)
        {
            if (b == null || b.Length < 16)
            {
                return null;
            }

            if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)
            {
                return Png(b);
            }

            if (b[0] == 0xFF && b[1] == 0xD8)
            {
                return Jpeg(b);
            }

            if (b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P')
            {
                return Webp(b);
            }

            return null;
        }

        private static (int, int)? Png(byte[] b)
        {
            if (b.Length < 24)
            {
                return null;
            }

            // 8 byte chu ky + 4 do dai + "IHDR" → rong o byte 16, cao o byte 20.
            int w = BigEndian(b, 16);
            int h = BigEndian(b, 20);
            return HopLe(w, h);
        }

        private static (int, int)? Jpeg(byte[] b)
        {
            int i = 2;
            while (i + 9 < b.Length)
            {
                if (b[i] != 0xFF)
                {
                    i++;
                    continue;
                }

                byte marker = b[i + 1];
                if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7) || marker == 0xFF)
                {
                    i += marker == 0xFF ? 1 : 2;
                    continue;
                }

                int doDai = (b[i + 2] << 8) | b[i + 3];
                bool laSof = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
                if (laSof)
                {
                    // FF Cx | do dai(2) | do sau bit(1) | cao(2) | rong(2)
                    int h = (b[i + 5] << 8) | b[i + 6];
                    int w = (b[i + 7] << 8) | b[i + 8];
                    return HopLe(w, h);
                }

                if (doDai < 2)
                {
                    return null;
                }

                i += 2 + doDai;
            }

            return null;
        }

        private static (int, int)? Webp(byte[] b)
        {
            if (b.Length < 30)
            {
                return null;
            }

            string loai = System.Text.Encoding.ASCII.GetString(b, 12, 4);

            if (loai == "VP8 ")
            {
                // Lossy: khung bat dau o byte 20; 3 byte tag + 3 byte ma bat dau (9D 01 2A), roi rong, cao 14 bit.
                int w = (b[26] | (b[27] << 8)) & 0x3FFF;
                int h = (b[28] | (b[29] << 8)) & 0x3FFF;
                return HopLe(w, h);
            }

            if (loai == "VP8L")
            {
                // Lossless: byte 20 la 0x2F, roi 14 bit (rong - 1) va 14 bit (cao - 1).
                int bits = b[21] | (b[22] << 8) | (b[23] << 16) | (b[24] << 24);
                int w = (bits & 0x3FFF) + 1;
                int h = ((bits >> 14) & 0x3FFF) + 1;
                return HopLe(w, h);
            }

            if (loai == "VP8X")
            {
                // Mo rong: (rong - 1) 24 bit o byte 24, (cao - 1) 24 bit o byte 27.
                int w = (b[24] | (b[25] << 8) | (b[26] << 16)) + 1;
                int h = (b[27] | (b[28] << 8) | (b[29] << 16)) + 1;
                return HopLe(w, h);
            }

            return null;
        }

        private static int BigEndian(byte[] b, int i)
        {
            return (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];
        }

        private static (int, int)? HopLe(int w, int h)
        {
            if (w <= 0 || h <= 0 || w > 100000 || h > 100000)
            {
                return null;
            }

            return (w, h);
        }
    }
}
