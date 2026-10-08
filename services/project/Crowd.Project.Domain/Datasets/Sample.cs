using System;
using Crowd.Labeling;
using Crowd.Project.Domain.Common;

namespace Crowd.Project.Domain.Datasets
{
    /// <summary>
    /// Mot MAU can gan nhan, o moi loai du lieu:
    ///
    ///   image / audio / video : FILE trong MinIO — database chi giu KHOA (StorageKey),
    ///                           muon xem phai xin link co han (S-07).
    ///   text / pair           : NOI DUNG luu thang trong Content (jsonb):
    ///                           {"text":"..."} hoac {"prompt":"...","a":"...","b":"..."}.
    ///
    /// Metadata (jsonb) giu thong tin de kiem nhan: kich thuoc anh, thoi luong,
    /// do dai van ban — xem Crowd.Labeling.SampleMetadata.
    ///
    /// Audio/video dai co the bi CAT thanh nhieu mau (doan): cac doan dung CHUNG
    /// mot StorageKey, Metadata ghi segmentStart / segmentEnd.
    /// </summary>
    public sealed class Sample
    {

        private Sample()
        {
            Modality = string.Empty;
            OriginalName = string.Empty;
            Sha256 = string.Empty;
            Metadata = SampleMetadata.Rong.ToRawJson();
        }

        public Guid Id { get; private set; }

        public Guid ProjectId { get; private set; }

        public Guid DatasetId { get; private set; }

        /// <summary>image | text | audio | video | pair — trung loai du lieu cua du an.</summary>
        public string Modality { get; private set; }

        /// <summary>Khoa file trong bucket datasets. null voi text / pair.</summary>
        public string? StorageKey { get; private set; }

        /// <summary>Noi dung voi text / pair. null voi du lieu la file.</summary>
        public RawJson? Content { get; private set; }

        /// <summary>Thong tin mau (Crowd.Labeling.SampleMetadata).</summary>
        public RawJson Metadata { get; private set; }

        /// <summary>Ten file goc / ten dong trong manifest — de doanh nghiep doi chieu voi du lieu goc.</summary>
        public string OriginalName { get; private set; }

        /// <summary>MIME cua file. null voi text / pair.</summary>
        public string? ContentType { get; private set; }

        /// <summary>Dung luong file. null voi text / pair.</summary>
        public long? SizeBytes { get; private set; }

        /// <summary>
        /// Dau van tay noi dung (SHA-256). UNIQUE theo du an: cung mot noi dung
        /// nap hai lan chi thanh mot mau — khong tra tien gan nhan hai lan. Doan
        /// cat tu file dai: bam cua (file + vi tri doan).
        /// </summary>
        public string Sha256 { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        /// <summary>
        /// Anh trong ZIP: he thong tu dat khoa "{projectId}/{sampleId}{duoi}" — khong
        /// dung ten file cua nguoi dung (co the chua "../" hay ky tu la).
        /// </summary>
        public static Sample Tao(
            Guid projectId,
            Guid datasetId,
            string originalName,
            string contentType,
            string extension,
            long sizeBytes,
            string sha256,
            DateTimeOffset luc,
            QuyDinhDuLieu quyDinh)
        {
            return TaoAnhTrongZip(projectId, datasetId, originalName, contentType, extension, sizeBytes, sha256, SampleMetadata.Rong, luc, quyDinh);
        }

        public static Sample TaoAnhTrongZip(
            Guid projectId,
            Guid datasetId,
            string originalName,
            string contentType,
            string extension,
            long sizeBytes,
            string sha256,
            SampleMetadata metadata,
            DateTimeOffset luc,
            QuyDinhDuLieu quyDinh)
        {
            Sample s = TaoKhung(projectId, datasetId, Modalities.Image, originalName, sha256, metadata, luc, quyDinh);
            s.StorageKey = projectId.ToString() + "/" + s.Id.ToString() + extension;
            s.ContentType = contentType;
            s.SizeBytes = sizeBytes;
            return s;
        }

        /// <summary>File da nam san trong MinIO (client upload thang bang link ky san).</summary>
        public static Sample TaoTuFile(
            Guid projectId,
            Guid datasetId,
            string modality,
            string storageKey,
            string originalName,
            string contentType,
            long sizeBytes,
            string sha256,
            SampleMetadata metadata,
            DateTimeOffset luc,
            QuyDinhDuLieu quyDinh)
        {
            if (!Modalities.LaFile(modality))
            {
                throw new InvalidValueException("loai_du_lieu_khong_phai_file", "Du lieu " + modality + " khong luu thanh file.");
            }

            if (string.IsNullOrWhiteSpace(storageKey) || !storageKey.StartsWith(projectId.ToString() + "/", StringComparison.Ordinal))
            {
                // Chan dung file cua du an khac (BOLA).
                throw new InvalidValueException("file_khong_thuoc_du_an", "File khong thuoc du an nay.");
            }

            Sample s = TaoKhung(projectId, datasetId, modality, originalName, sha256, metadata, luc, quyDinh);
            s.StorageKey = storageKey;
            s.ContentType = contentType;
            s.SizeBytes = sizeBytes;
            return s;
        }

        /// <summary>Text / pair: noi dung nam ngay trong database.</summary>
        public static Sample TaoTuNoiDung(
            Guid projectId,
            Guid datasetId,
            string modality,
            RawJson content,
            string originalName,
            string sha256,
            SampleMetadata metadata,
            DateTimeOffset luc,
            QuyDinhDuLieu quyDinh)
        {
            if (Modalities.LaFile(modality))
            {
                throw new InvalidValueException("loai_du_lieu_la_file", "Du lieu " + modality + " phai la file.");
            }

            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            Sample s = TaoKhung(projectId, datasetId, modality, originalName, sha256, metadata, luc, quyDinh);
            s.Content = content;
            return s;
        }

        private static Sample TaoKhung(
            Guid projectId, Guid datasetId, string modality, string originalName, string sha256, SampleMetadata metadata, DateTimeOffset luc, QuyDinhDuLieu quyDinh)
        {
            if (metadata == null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }

            string ten = originalName == null ? string.Empty : originalName.Trim();
            if (ten.Length > quyDinh.DoDaiTenMauToiDa)
            {
                ten = ten.Substring(0, quyDinh.DoDaiTenMauToiDa);
            }

            Sample s = new Sample();
            s.Id = Guid.CreateVersion7();
            s.ProjectId = projectId;
            s.DatasetId = datasetId;
            s.Modality = modality;
            s.OriginalName = ten;
            s.Sha256 = sha256;
            s.Metadata = metadata.ToRawJson();
            s.CreatedAt = luc;
            return s;
        }
    }
}
