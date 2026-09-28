using System;

namespace Crowd.Project.Domain.Datasets
{
    /// <summary>
    /// Mot mau can gan nhan — o day la mot anh nam trong MinIO.
    ///
    /// Database chi giu KHOA toi file, khong giu chinh anh. Client khong bao
    /// gio thay khoa nay truc tiep (P-01); muon xem anh phai xin mot link co
    /// han 5 phut (S-07).
    /// </summary>
    public sealed class Sample
    {
        private Sample()
        {
            StorageKey = string.Empty;
            OriginalName = string.Empty;
            ContentType = string.Empty;
            Sha256 = string.Empty;
        }

        public Guid Id { get; private set; }

        public Guid ProjectId { get; private set; }

        public Guid DatasetId { get; private set; }

        /// <summary>Vd "0199.../0199...ab.jpg" trong bucket datasets.</summary>
        public string StorageKey { get; private set; }

        /// <summary>Ten file trong ZIP — de doanh nghiep doi chieu voi du lieu goc.</summary>
        public string OriginalName { get; private set; }

        public string ContentType { get; private set; }

        public long SizeBytes { get; private set; }

        /// <summary>
        /// Dau van tay noi dung. UNIQUE theo du an: cung mot anh nap hai lan
        /// (hai file ten khac nhau) chi thanh mot mau — khong tra tien gan nhan
        /// hai lan cho cung mot anh.
        /// </summary>
        public string Sha256 { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public static Sample Tao(
            Guid projectId,
            Guid datasetId,
            string originalName,
            string contentType,
            string extension,
            long sizeBytes,
            string sha256,
            DateTimeOffset luc)
        {
            Sample s = new Sample();
            s.Id = Guid.CreateVersion7();
            s.ProjectId = projectId;
            s.DatasetId = datasetId;

            // Khoa do HE THONG dat, khong dung ten file cua nguoi dung: ten file
            // co the chua "../" hay ky tu la.
            s.StorageKey = projectId.ToString() + "/" + s.Id.ToString() + extension;

            s.OriginalName = originalName;
            s.ContentType = contentType;
            s.SizeBytes = sizeBytes;
            s.Sha256 = sha256;
            s.CreatedAt = luc;
            return s;
        }
    }
}
