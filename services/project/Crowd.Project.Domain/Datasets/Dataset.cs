using System;
using Crowd.Labeling;
using Crowd.Project.Domain.Common;

namespace Crowd.Project.Domain.Datasets
{
    public enum DatasetStatus
    {
        /// <summary>Lo manifest vua tao, cho worker xu ly.</summary>
        Pending,

        /// <summary>Worker dang doc file, do metadata, tao mau.</summary>
        Ingesting,

        /// <summary>Xong — co it nhat mot mau hop le.</summary>
        Ready,

        /// <summary>Khong tao duoc mau nao (xem ErrorSummary).</summary>
        Failed,
    }

    /// <summary>
    /// Mot lan nap du lieu (FB-11). Hai cach:
    ///
    ///   ZIP anh       : xu ly NGAY trong request (anh nho) → Ready.
    ///   Manifest      : danh sach dong (file da upload thang len MinIO, hoac noi
    ///                   dung text) → Pending, worker nen xu ly (doc file, do thoi
    ///                   luong bang ffprobe, cat doan) → Ready / Failed.
    ///
    /// Du an nap duoc nhieu dataset khi con Nhap.
    /// </summary>
    public sealed class Dataset
    {
        public const int DoDaiTenToiDa = 200;
        public const int DoDaiLoiToiDa = 4000;

        private Dataset()
        {
            Name = string.Empty;
        }

        public Guid Id { get; private set; }

        public Guid ProjectId { get; private set; }

        public string Name { get; private set; }

        public DatasetStatus Status { get; private set; }

        public int SampleCount { get; private set; }

        /// <summary>So file / dong bi bo qua: sai dinh dang, qua lon, trung noi dung da co.</summary>
        public int SkippedCount { get; private set; }

        /// <summary>Tom tat ly do cac dong bi bo qua (hoac ly do that bai).</summary>
        public string? ErrorSummary { get; private set; }

        /// <summary>Cac dong manifest gui KEM request (it dong). null khi manifest nam trong MinIO.</summary>
        public RawJson? Manifest { get; private set; }

        /// <summary>Khoa file manifest (.jsonl) trong MinIO, khi manifest lon. null khi gui kem request.</summary>
        public string? ManifestKey { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public DateTimeOffset? FinishedAt { get; private set; }

        /// <summary>Lo ZIP anh — xu ly ngay, GhiNhanKetQuaNap chot ket qua.</summary>
        public static Dataset Tao(Guid projectId, string name, DateTimeOffset luc)
        {
            Dataset d = TaoKhung(projectId, name, luc);
            d.Status = DatasetStatus.Ready;
            return d;
        }

        /// <summary>Lo manifest — cho worker. Dung MOT trong hai: dong gui kem hoac khoa file manifest.</summary>
        public static Dataset TaoTuManifest(Guid projectId, string name, RawJson? manifest, string? manifestKey, DateTimeOffset luc)
        {
            bool coDong = manifest != null;
            bool coFile = !string.IsNullOrWhiteSpace(manifestKey);

            if (coDong == coFile)
            {
                throw new InvalidValueException("manifest_khong_hop_le", "Can DUNG MOT trong hai: \"rows\" hoac \"manifestKey\".");
            }

            if (coFile && !manifestKey!.StartsWith(projectId.ToString() + "/", StringComparison.Ordinal))
            {
                throw new InvalidValueException("file_khong_thuoc_du_an", "File manifest khong thuoc du an nay.");
            }

            Dataset d = TaoKhung(projectId, name, luc);
            d.Status = DatasetStatus.Pending;
            d.Manifest = manifest;
            d.ManifestKey = coFile ? manifestKey : null;
            return d;
        }

        public void GhiNhanKetQuaNap(int soMau, int soBoQua)
        {
            if (soMau <= 0)
            {
                throw new RuleViolationException(
                    "khong_co_anh_hop_le",
                    "File ZIP khong co anh hop le nao (chi nhan jpg, png, webp).");
            }

            SampleCount = soMau;
            SkippedCount = soBoQua;
        }

        public bool DangXuLy()
        {
            return Status == DatasetStatus.Pending || Status == DatasetStatus.Ingesting;
        }

        public void BatDauXuLy()
        {
            if (Status != DatasetStatus.Pending)
            {
                throw new RuleViolationException("dataset_khong_cho", "Dataset khong o trang thai cho xu ly (" + Status + ").");
            }

            Status = DatasetStatus.Ingesting;
        }

        /// <summary>Worker xu ly xong. Khong co mau hop le nao thi Failed.</summary>
        public void HoanTat(int soMau, int soBoQua, string? tomTatLoi, DateTimeOffset luc)
        {
            if (Status != DatasetStatus.Ingesting)
            {
                throw new RuleViolationException("dataset_khong_dang_xu_ly", "Dataset khong dang xu ly (" + Status + ").");
            }

            SampleCount = soMau;
            SkippedCount = soBoQua;
            ErrorSummary = CatNgan(tomTatLoi);
            Status = soMau > 0 ? DatasetStatus.Ready : DatasetStatus.Failed;
            FinishedAt = luc;
        }

        public void ThatBai(string lyDo, DateTimeOffset luc)
        {
            ErrorSummary = CatNgan(lyDo);
            Status = DatasetStatus.Failed;
            FinishedAt = luc;
        }

        private static Dataset TaoKhung(Guid projectId, string name, DateTimeOffset luc)
        {
            string ten = name == null ? string.Empty : name.Trim();
            if (ten.Length == 0 || ten.Length > DoDaiTenToiDa)
            {
                throw new InvalidValueException(
                    "ten_dataset_khong_hop_le",
                    "Ten dataset phai tu 1 den " + DoDaiTenToiDa + " ky tu.");
            }

            Dataset d = new Dataset();
            d.Id = Guid.CreateVersion7();
            d.ProjectId = projectId;
            d.Name = ten;
            d.CreatedAt = luc;
            return d;
        }

        private static string? CatNgan(string? s)
        {
            if (s == null)
            {
                return null;
            }

            return s.Length <= DoDaiLoiToiDa ? s : s.Substring(0, DoDaiLoiToiDa);
        }
    }
}
