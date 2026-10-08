using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Project.Domain.Common;

namespace Crowd.Project.Infrastructure.Datasets
{
    /// <summary>Mot anh doc duoc tu ZIP, da kiem.</summary>
    public sealed class ZipImage
    {
        public ZipImage(string originalName, string extension, string contentType, byte[] content, string sha256)
        {
            OriginalName = originalName;
            Extension = extension;
            ContentType = contentType;
            Content = content;
            Sha256 = sha256;
        }

        public string OriginalName { get; }

        /// <summary>Duoi file chuan hoa: ".jpg", ".png", ".webp".</summary>
        public string Extension { get; }

        public string ContentType { get; }

        public byte[] Content { get; }

        public string Sha256 { get; }
    }

    /// <summary>Gioi han khi doc ZIP — chong zip bomb va file rac.</summary>
    public sealed class ZipLimits
    {
        /// <summary>Setting dataset.zip_max_entries.</summary>
        public required int MaxEntries { get; init; }

        /// <summary>Setting dataset.zip_image_max_bytes.</summary>
        public required long MaxImageBytes { get; init; }

        /// <summary>Tong dung luong SAU giai nen — setting dataset.zip_total_max_bytes.</summary>
        public required long MaxTotalBytes { get; init; }
    }

    /// <summary>
    /// Doc file ZIP anh do doanh nghiep upload. File tu nguoi dung la DU LIEU
    /// KHONG TIN CAY, nen moi thu deu kiem:
    ///
    ///   - Dem so entry TRUOC khi doc — ZIP 1 trieu file rong la tan cong.
    ///   - Doc tung entry co TRAN: kich thuoc ghi trong header ZIP co the noi
    ///     doi (zip bomb: 1 MB nen thanh 10 GB), nen dem byte THAT khi doc.
    ///   - Kiem MAGIC BYTES (vai byte dau file) chu khong tin duoi file: doi
    ///     "virus.exe" thanh "anh.jpg" van bi loai.
    ///   - Bo qua thu muc, file an, rac cua macOS (__MACOSX/, ._abc.jpg).
    ///
    /// File khong hop le bi BO QUA va dem lai (khong lam hong ca lan nap);
    /// vuot gioi han tong thi TU CHOI ca file.
    /// </summary>
    public static class ZipImageReader
    {
        public static async Task<int> DuyetAsync(
            Stream zip,
            ZipLimits gioiHan,
            Func<ZipImage, Task> xuLyAnh,
            CancellationToken ct)
        {
            if (zip == null)
            {
                throw new ArgumentNullException(nameof(zip));
            }

            if (gioiHan == null)
            {
                throw new ArgumentNullException(nameof(gioiHan));
            }

            if (xuLyAnh == null)
            {
                throw new ArgumentNullException(nameof(xuLyAnh));
            }

            ZipArchive archive;
            try
            {
                archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
            }
            catch (InvalidDataException)
            {
                throw new InvalidValueException("khong_phai_zip", "File tai len khong phai ZIP hop le.");
            }

            using (archive)
            {
                if (archive.Entries.Count > gioiHan.MaxEntries)
                {
                    throw new InvalidValueException(
                        "zip_qua_nhieu_file",
                        "ZIP toi da " + gioiHan.MaxEntries + " file.");
                }

                int soBoQua = 0;
                long tongByte = 0;

                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    ct.ThrowIfCancellationRequested();

                    if (LaRac(entry))
                    {
                        continue;
                    }

                    string duoi = Path.GetExtension(entry.Name).ToLowerInvariant();
                    string? loai = LoaiNoiDungTheoDuoi(duoi);

                    if (loai == null || entry.Length > gioiHan.MaxImageBytes)
                    {
                        soBoQua = soBoQua + 1;
                        continue;
                    }

                    byte[]? noiDung = await DocCoTranAsync(entry, gioiHan.MaxImageBytes, ct).ConfigureAwait(false);

                    if (noiDung == null || !DungMagicBytes(noiDung, duoi))
                    {
                        soBoQua = soBoQua + 1;
                        continue;
                    }

                    tongByte = tongByte + noiDung.Length;
                    if (tongByte > gioiHan.MaxTotalBytes)
                    {
                        throw new InvalidValueException("zip_qua_lon", "Tong dung luong anh vuot gioi han.");
                    }

                    string ten = entry.FullName.Length > 500 ? entry.FullName.Substring(0, 500) : entry.FullName;
                    string duoiChuan = duoi == ".jpeg" ? ".jpg" : duoi;
                    string sha = Convert.ToHexStringLower(SHA256.HashData(noiDung));

                    await xuLyAnh(new ZipImage(ten, duoiChuan, loai, noiDung, sha)).ConfigureAwait(false);
                }

                return soBoQua;
            }
        }

        private static bool LaRac(ZipArchiveEntry entry)
        {
            // Thu muc: ten rong (FullName ket thuc bang "/").
            if (entry.Name.Length == 0)
            {
                return true;
            }

            if (entry.FullName.StartsWith("__MACOSX/", StringComparison.Ordinal))
            {
                return true;
            }

            // File an: ".DS_Store", "._anh.jpg" (macOS), ".thumbs"...
            return entry.Name.StartsWith('.');
        }

        private static string? LoaiNoiDungTheoDuoi(string duoi)
        {
            if (duoi == ".jpg" || duoi == ".jpeg")
            {
                return "image/jpeg";
            }

            if (duoi == ".png")
            {
                return "image/png";
            }

            if (duoi == ".webp")
            {
                return "image/webp";
            }

            return null;
        }

        /// <summary>Doc entry nhung dung lai ngay khi vuot tran. null = qua lon.</summary>
        private static async Task<byte[]?> DocCoTranAsync(ZipArchiveEntry entry, long tran, CancellationToken ct)
        {
            using (Stream nguon = await entry.OpenAsync(ct).ConfigureAwait(false))
            using (MemoryStream dich = new MemoryStream())
            {
                byte[] bo = new byte[81920];
                long daDoc = 0;

                while (true)
                {
                    int n = await nguon.ReadAsync(bo.AsMemory(), ct).ConfigureAwait(false);
                    if (n == 0)
                    {
                        break;
                    }

                    daDoc = daDoc + n;
                    if (daDoc > tran)
                    {
                        return null;
                    }

                    dich.Write(bo, 0, n);
                }

                return dich.ToArray();
            }
        }

        /// <summary>Vai byte dau file cho biet dinh dang THAT, bat ke duoi file.</summary>
        public static bool DungMagicBytes(byte[] b, string duoi)
        {
            if (b == null)
            {
                return false;
            }

            if (duoi == ".jpg" || duoi == ".jpeg")
            {
                return b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;
            }

            if (duoi == ".png")
            {
                byte[] png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
                if (b.Length < png.Length)
                {
                    return false;
                }

                for (int i = 0; i < png.Length; i++)
                {
                    if (b[i] != png[i])
                    {
                        return false;
                    }
                }

                return true;
            }

            if (duoi == ".webp")
            {
                // "RIFF" ???? "WEBP"
                return b.Length >= 12
                       && b[0] == (byte)'R' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'F'
                       && b[8] == (byte)'W' && b[9] == (byte)'E' && b[10] == (byte)'B' && b[11] == (byte)'P';
            }

            return false;
        }
    }
}
