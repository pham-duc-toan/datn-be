using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Crowd.Seeding
{
    /// <summary>
    /// Sinh anh PNG mot mau ngay trong code — repo khong phai chua file anh mau,
    /// va moi lan seed ra dung tung byte (cung SHA-256), nen chay lai khong tao
    /// anh "trung noi dung" khac.
    ///
    /// Anh co vien sang hon 8px de nhin ra la anh that chu khong phai o mau.
    ///
    /// Dinh dang PNG toi gian (RFC 2083):
    ///   chu ky 8 byte | IHDR | IDAT (zlib cua cac dong diem anh) | IEND
    /// Moi khoi: do dai (4 byte) | loai (4 ky tu) | du lieu | CRC-32 cua (loai + du lieu).
    /// </summary>
    public static class AnhMauPng
    {
        public const string ContentType = "image/png";

        private const int VienPx = 8;

        private static readonly byte[] ChuKy = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };

        private static readonly uint[] BangCrc = TaoBangCrc();

        public static byte[] Tao(byte r, byte g, byte b, int kichThuoc)
        {
            if (kichThuoc < 2 * VienPx + 1 || kichThuoc > 2048)
            {
                throw new ArgumentOutOfRangeException(nameof(kichThuoc), kichThuoc, "Kich thuoc anh 17-2048 px.");
            }

            using (MemoryStream ra = new MemoryStream())
            {
                ra.Write(ChuKy, 0, ChuKy.Length);

                // IHDR: rong, cao, 8 bit/kenh, kieu mau 2 = RGB, nen 0, loc 0, khong xen ke.
                byte[] ihdr = new byte[13];
                GhiSoNguyen(ihdr, 0, (uint)kichThuoc);
                GhiSoNguyen(ihdr, 4, (uint)kichThuoc);
                ihdr[8] = 8;
                ihdr[9] = 2;
                GhiKhoi(ra, "IHDR", ihdr);

                GhiKhoi(ra, "IDAT", NenDiemAnh(r, g, b, kichThuoc));
                GhiKhoi(ra, "IEND", Array.Empty<byte>());

                return ra.ToArray();
            }
        }

        private static byte[] NenDiemAnh(byte r, byte g, byte b, int n)
        {
            // Vien: pha 50% voi mau trang.
            byte vr = (byte)((r + 255) / 2);
            byte vg = (byte)((g + 255) / 2);
            byte vb = (byte)((b + 255) / 2);

            // Moi dong: 1 byte "kieu loc" (0 = khong loc) + n diem x 3 byte.
            byte[] tho = new byte[n * (1 + n * 3)];
            int i = 0;

            for (int y = 0; y < n; y++)
            {
                tho[i] = 0;
                i = i + 1;

                for (int x = 0; x < n; x++)
                {
                    bool laVien = x < VienPx || y < VienPx || x >= n - VienPx || y >= n - VienPx;
                    tho[i] = laVien ? vr : r;
                    tho[i + 1] = laVien ? vg : g;
                    tho[i + 2] = laVien ? vb : b;
                    i = i + 3;
                }
            }

            using (MemoryStream nen = new MemoryStream())
            {
                // ZLibStream: dinh dang zlib (header + deflate + adler32) dung nhu PNG yeu cau.
                using (ZLibStream z = new ZLibStream(nen, CompressionLevel.Optimal, true))
                {
                    z.Write(tho, 0, tho.Length);
                }

                return nen.ToArray();
            }
        }

        private static void GhiKhoi(Stream ra, string loai, byte[] duLieu)
        {
            byte[] doDai = new byte[4];
            GhiSoNguyen(doDai, 0, (uint)duLieu.Length);
            ra.Write(doDai, 0, 4);

            byte[] loaiVaDuLieu = new byte[4 + duLieu.Length];
            Encoding.ASCII.GetBytes(loai, 0, 4, loaiVaDuLieu, 0);
            Array.Copy(duLieu, 0, loaiVaDuLieu, 4, duLieu.Length);
            ra.Write(loaiVaDuLieu, 0, loaiVaDuLieu.Length);

            byte[] crc = new byte[4];
            GhiSoNguyen(crc, 0, Crc32(loaiVaDuLieu));
            ra.Write(crc, 0, 4);
        }

        /// <summary>PNG ghi so nguyen kieu big-endian (byte cao truoc).</summary>
        private static void GhiSoNguyen(byte[] dich, int viTri, uint giaTri)
        {
            dich[viTri] = (byte)(giaTri >> 24);
            dich[viTri + 1] = (byte)(giaTri >> 16);
            dich[viTri + 2] = (byte)(giaTri >> 8);
            dich[viTri + 3] = (byte)giaTri;
        }

        private static uint Crc32(byte[] duLieu)
        {
            uint c = 0xFFFFFFFFu;
            foreach (byte b in duLieu)
            {
                c = BangCrc[(c ^ b) & 0xFF] ^ (c >> 8);
            }

            return c ^ 0xFFFFFFFFu;
        }

        private static uint[] TaoBangCrc()
        {
            uint[] bang = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                }

                bang[n] = c;
            }

            return bang;
        }
    }
}
