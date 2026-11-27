using System;
using System.Security.Cryptography;
using System.Text;
using Crowd.Project.Domain.Common;

namespace Crowd.Project.Domain.Datasets
{
    /// <summary>
    /// PHIEU GIAI NEN mot file ZIP anh (kien truc 3.41). ZIP khong con di qua request: doanh nghiep xin link ky san,
    /// PUT thang len MinIO ("{projectId}/zip/{guid}.zip"), roi dang ky phieu nay (202). Worker giai nen nen:
    ///
    ///   queued → processing → done | failed
    ///
    /// Moi anh hop le thanh mot mau ready (da kiem magic bytes, kich thuoc, bo trung) trong MOT SaveChanges — ZIP
    /// hong giua chung (vuot tong dung luong, khong phai ZIP) thi khong mau nao duoc ghi, giong luc con xu ly trong
    /// request. Xong (hay hong) thi xoa file ZIP khoi kho: anh da nam o khoa rieng cua tung mau.
    ///
    /// Mot dong cho moi FILE ZIP chu khong phai cho moi "lo nap" (bang datasets cu, 3.26): no chi la phieu viec
    /// cho worker va cho giao dien biet ZIP dang o dau.
    /// </summary>
    public sealed class ZipImport
    {
        private ZipImport()
        {
            StorageKey = string.Empty;
            OriginalName = string.Empty;
            State = TrangThaiZip.ChoXuLy;
        }

        public Guid Id { get; private set; }

        public Guid ProjectId { get; private set; }

        /// <summary>Khoa file ZIP trong kho: "{projectId}/zip/{guid}.zip" (he thong dat luc cap link upload).</summary>
        public string StorageKey { get; private set; }

        /// <summary>Ten file ZIP goc — chi de hien thi.</summary>
        public string OriginalName { get; private set; }

        public long SizeBytes { get; private set; }

        /// <summary>TrangThaiZip: queued | processing | done | failed.</summary>
        public string State { get; private set; }

        /// <summary>So file trong ZIP da doc (cap nhat dan khi dang giai nen — giao dien hien tien do).</summary>
        public int ProcessedCount { get; private set; }

        /// <summary>So anh hop le thanh mau (chot khi xong).</summary>
        public int ImageCount { get; private set; }

        /// <summary>So file bo qua: khong phai anh jpg / png / webp, qua lon, trung noi dung.</summary>
        public int SkippedCount { get; private set; }

        /// <summary>Ly do khi failed: khong phai ZIP, qua nhieu file, vuot tong dung luong, du an khong con Nhap...</summary>
        public string? Error { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public DateTimeOffset? StartedAt { get; private set; }

        public DateTimeOffset? FinishedAt { get; private set; }

        /// <summary>Tien to khoa ZIP cua mot du an — UploadService cap khoa, Tao kiem lai (chong dung file du an khac).</summary>
        public static string TienToKhoa(Guid projectId)
        {
            return projectId.ToString() + "/zip/";
        }

        public static ZipImport Tao(Guid projectId, string storageKey, string? tenGoc, long sizeBytes, DateTimeOffset luc, QuyDinhDuLieu quyDinh)
        {
            if (quyDinh == null)
            {
                throw new ArgumentNullException(nameof(quyDinh));
            }

            string khoa = storageKey == null ? string.Empty : storageKey.Trim();
            if (!khoa.StartsWith(TienToKhoa(projectId), StringComparison.Ordinal)
                || !khoa.EndsWith(".zip", StringComparison.Ordinal)
                || khoa.Contains("..", StringComparison.Ordinal)
                || khoa.Length > 300)
            {
                // Chan dung file cua du an khac / file khong phai ZIP da cap link (BOLA).
                throw new InvalidValueException("khoa_zip_khong_hop_le", "File ZIP không thuộc dự án này (dùng đúng key nhận được khi xin link upload).");
            }

            if (sizeBytes <= 0)
            {
                throw new InvalidValueException("zip_rong", "File ZIP rỗng.");
            }

            string ten = string.IsNullOrWhiteSpace(tenGoc) ? "anh.zip" : tenGoc.Trim();
            if (ten.Length > quyDinh.DoDaiTenMauToiDa)
            {
                ten = ten.Substring(0, quyDinh.DoDaiTenMauToiDa);
            }

            ZipImport z = new ZipImport();
            z.Id = Guid.CreateVersion7();
            z.ProjectId = projectId;
            z.StorageKey = khoa;
            z.OriginalName = ten;
            z.SizeBytes = sizeBytes;
            z.State = TrangThaiZip.ChoXuLy;
            z.CreatedAt = luc;
            return z;
        }

        public bool DangCho()
        {
            return State == TrangThaiZip.ChoXuLy || State == TrangThaiZip.DangXuLy;
        }

        /// <summary>Worker nhan viec (queued → processing). Lam lai sau khi khoi dong lai thi dem lai tu dau.</summary>
        public void BatDau(DateTimeOffset luc)
        {
            if (State != TrangThaiZip.ChoXuLy)
            {
                throw new RuleViolationException("zip_khong_cho_xu_ly", "Phiếu ZIP không ở trạng thái chờ (" + State + ").");
            }

            State = TrangThaiZip.DangXuLy;
            StartedAt = luc;
            ProcessedCount = 0;
        }

        /// <summary>Service tat giua chung: phieu dang giai nen ve hang doi de lam lai tu dau (chua mau nao duoc ghi).</summary>
        public void VeHangDoi()
        {
            if (State == TrangThaiZip.DangXuLy)
            {
                State = TrangThaiZip.ChoXuLy;
            }
        }

        public void HoanTat(int soAnh, int soBoQua, DateTimeOffset luc)
        {
            if (State != TrangThaiZip.DangXuLy)
            {
                throw new RuleViolationException("zip_khong_dang_xu_ly", "Phiếu ZIP không đang xử lý (" + State + ").");
            }

            if (soAnh < 0 || soBoQua < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(soAnh));
            }

            State = TrangThaiZip.Xong;
            ImageCount = soAnh;
            SkippedCount = soBoQua;
            ProcessedCount = soAnh + soBoQua;
            Error = null;
            FinishedAt = luc;
        }

        public void Loi(string lyDo, int doDaiToiDa, DateTimeOffset luc)
        {
            string ly = string.IsNullOrWhiteSpace(lyDo) ? "Không giải nén được file ZIP." : lyDo.Trim();
            Error = ly.Length > doDaiToiDa ? ly.Substring(0, doDaiToiDa) : ly;
            State = TrangThaiZip.Loi;
            ImageCount = 0;
            FinishedAt = luc;
        }

        /// <summary>
        /// Ma mau CO DINH cho anh thu <paramref name="viTri"/> trong ZIP nay: lam lai phieu (sau khi service khoi dong lai)
        /// thi anh ghi de dung khoa cu trong kho thay vi de lai file mo coi. Dang UUID v8 (RFC 9562) tu SHA-256.
        /// </summary>
        public Guid MaMauCuaAnh(int viTri)
        {
            if (viTri < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(viTri));
            }

            byte[] bam = SHA256.HashData(Encoding.UTF8.GetBytes(Id.ToString("N") + ":" + viTri.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            byte[] g = new byte[16];
            Array.Copy(bam, g, 16);

            // Big-endian theo RFC: byte 6 = version (8), byte 8 = variant (10xx).
            g[6] = (byte)((g[6] & 0x0F) | 0x80);
            g[8] = (byte)((g[8] & 0x3F) | 0x80);
            return new Guid(g, true);
        }
    }

    /// <summary>Gia tri cua ZipImport.State.</summary>
    public static class TrangThaiZip
    {
        /// <summary>File ZIP da len kho, phieu dang xep hang cho worker.</summary>
        public const string ChoXuLy = "queued";

        /// <summary>Worker dang tai ZIP ve va giai nen, kiem tung anh.</summary>
        public const string DangXuLy = "processing";

        /// <summary>Da giai nen xong: anh hop le da thanh mau.</summary>
        public const string Xong = "done";

        public const string Loi = "failed";
    }
}
