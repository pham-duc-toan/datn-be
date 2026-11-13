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
    ///   text / pair / list    : NOI DUNG luu thang trong Content (jsonb):
    ///                           {"text":"..."}, {"a":"...","b":"..."} hoac {"items":[...]}.
    ///
    /// Question: cau hoi RIENG cua mau (vd hoi dap ve anh: moi anh mot cau). null = dung cau hoi chung cua du an
    /// (label_schema.question) — khong chep cau chung vao tung mau.
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
            IngestState = TrangThaiNap.SanSang;
            Metadata = SampleMetadata.Rong.ToRawJson();
        }

        public Guid Id { get; private set; }

        public Guid ProjectId { get; private set; }

        /// <summary>image | text | audio | video | pair — trung loai du lieu cua du an.</summary>
        public string Modality { get; private set; }

        /// <summary>Khoa file trong bucket datasets. null voi text / pair.</summary>
        public string? StorageKey { get; private set; }

        /// <summary>Noi dung voi text / pair. null voi du lieu la file.</summary>
        public RawJson? Content { get; private set; }

        /// <summary>Thong tin mau (Crowd.Labeling.SampleMetadata).</summary>
        public RawJson Metadata { get; private set; }

        /// <summary>
        /// Cau hoi rieng cua mau, hien thay cau hoi chung. Chong trung tinh theo (Sha256, Question): cung mot anh
        /// voi hai cau hoi khac nhau la hai mau.
        /// </summary>
        public string? Question { get; private set; }

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
        public string? Sha256 { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        /// <summary>
        /// Tien trinh NAP mau (TrangThaiNap): queued → processing → ready | failed. File upload xong chi moi
        /// la mot dong "cho xu ly"; worker moi doc file, do thong tin, dedupe roi chot ready. Cac truy van
        /// nghiep vu chi thay mau ready (bo loc toan cuc trong ProjectDbContext).
        /// </summary>
        public string IngestState { get; private set; }

        /// <summary>Ly do khi IngestState = failed (file hong, sai loai, trung noi dung, khong tai duoc...).</summary>
        public string? IngestError { get; private set; }

        /// <summary>
        /// Nap tu nguon ngoai (URL / Google Drive / S3 da ky san): worker tai file tu day vao StorageKey roi xoa
        /// gia tri nay. null voi file client da upload thang len MinIO.
        /// </summary>
        public string? IngestSource { get; private set; }

        /// <summary>
        /// Video: media-svc chuan bi ban chuan (watermark chi nhung khung dau + cuoi) — TrangThaiMedia.
        /// null = khong can (khong phai video, hoac video nap truoc khi co buoc nay).
        /// </summary>
        public string? MediaState { get; private set; }

        /// <summary>Ly do khi MediaState = failed.</summary>
        public string? MediaError { get; private set; }

        /// <summary>
        /// Luc mau bi LOAI khoi du an do bao loi (chu du an loai, hoac du nguoi bao tu loai). null = con dung.
        /// Mau bi loai khong vao bai lam moi va bi bo khoi phan cham (project.collection_finished mang
        /// ExcludedSampleIds) — ai da tra loi cau do cung khong bi tinh.
        /// </summary>
        public DateTimeOffset? ExcludedAt { get; private set; }

        /// <summary>report_accepted (chu du an loai) | auto_reports (du nguoi bao).</summary>
        public string? ExcludedReason { get; private set; }

        /// <summary>
        /// Dat / xoa cau hoi rieng (chuoi rong = xoa, dung lai cau hoi chung). Chi khi du an con Nhap — service
        /// kiem truoc (LabelingProject.KiemTraCoTheNapDuLieu): doi cau hoi giua chung thi bai nop truoc va sau tra
        /// loi hai cau khac nhau.
        /// </summary>
        public void DatCauHoi(string? cauHoi)
        {
            string? q = cauHoi == null ? null : cauHoi.Trim();
            if (string.IsNullOrEmpty(q))
            {
                Question = null;
                return;
            }

            if (q.Length > CotDb.CauHoiMau)
            {
                throw new InvalidValueException(
                    "cau_hoi_qua_dai",
                    "Câu hỏi tối đa " + CotDb.CauHoiMau + " ký tự.");
            }

            Question = q;
        }

        /// <summary>Cau hoi labeler THAY cho mau nay: cau rieng neu co, khong thi cau hoi chung cua du an.</summary>
        public string? CauHoiHienThi(LabelSchema? tapNhan)
        {
            if (Question != null)
            {
                return Question;
            }

            return tapNhan == null ? null : tapNhan.Question;
        }

        /// <summary>Loai mau (mot lan). Tra ve false neu da bi loai tu truoc.</summary>
        public bool LoaiBo(string lyDo, DateTimeOffset luc)
        {
            if (string.IsNullOrWhiteSpace(lyDo))
            {
                throw new ArgumentException("Thieu ly do.", nameof(lyDo));
            }

            if (ExcludedAt.HasValue)
            {
                return false;
            }

            ExcludedAt = luc;
            ExcludedReason = lyDo;
            return true;
        }

        /// <summary>media-svc bao ket qua (video.prepared). Giao lai nhieu lan thi ghi de — vo hai.</summary>
        public void GhiKetQuaMedia(bool thanhCong, string? loi)
        {
            MediaState = thanhCong ? TrangThaiMedia.SanSang : TrangThaiMedia.Loi;
            if (thanhCong || loi == null)
            {
                MediaError = null;
            }
            else
            {
                MediaError = loi.Length > CotDb.LoiMedia ? loi.Substring(0, CotDb.LoiMedia) : loi;
            }
        }

        // =====================================================================
        // TIEN TRINH NAP (queued → processing → ready | failed)
        // =====================================================================

        public bool DangCho()
        {
            return IngestState == TrangThaiNap.ChoXuLy;
        }

        /// <summary>Worker nhan viec (queued → processing).</summary>
        public void BatDauNap()
        {
            if (IngestState != TrangThaiNap.ChoXuLy)
            {
                throw new RuleViolationException("mau_khong_cho_nap", "Mẫu không ở trạng thái chờ xử lý (" + IngestState + ").");
            }

            IngestState = TrangThaiNap.DangXuLy;
        }

        /// <summary>Service khoi dong lai giua chung: mau dang xu ly do duoc dua ve hang doi de lam lai.</summary>
        public void VeHangDoi()
        {
            if (IngestState == TrangThaiNap.DangXuLy)
            {
                IngestState = TrangThaiNap.ChoXuLy;
            }
        }

        /// <summary>Worker doc xong file: chot thong tin that cua mau → ready. Video con phai cho media-svc chuan hoa.</summary>
        public void HoanTatNap(string sha256, long sizeBytes, string contentType, SampleMetadata metadata)
        {
            if (IngestState != TrangThaiNap.DangXuLy)
            {
                throw new RuleViolationException("mau_khong_dang_nap", "Mẫu không đang xử lý (" + IngestState + ").");
            }

            if (string.IsNullOrWhiteSpace(sha256))
            {
                throw new ArgumentException("Thieu dau van tay.", nameof(sha256));
            }

            if (metadata == null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }

            Sha256 = sha256;
            SizeBytes = sizeBytes;
            ContentType = contentType;
            Metadata = metadata.ToRawJson();
            IngestState = TrangThaiNap.SanSang;
            IngestError = null;
            IngestSource = null;
            if (Modality == Modalities.Video)
            {
                MediaState = TrangThaiMedia.DangChuanBi;
            }
        }

        /// <summary>Mau goc giu doan cat dau tien: them "[bat dau-ket thuc s]" vao ten.</summary>
        public void DatTen(string ten, QuyDinhDuLieu quyDinh)
        {
            string t = ten == null ? string.Empty : ten.Trim();
            OriginalName = t.Length > quyDinh.DoDaiTenMauToiDa ? t.Substring(0, quyDinh.DoDaiTenMauToiDa) : t;
        }

        /// <summary>File hong / sai loai / trung noi dung / khong tai duoc: mau o lai de chu du an thay ly do.</summary>
        public void NapLoi(string lyDo, int doDaiToiDa)
        {
            string ly = string.IsNullOrWhiteSpace(lyDo) ? "Không xử lý được file." : lyDo.Trim();
            IngestError = ly.Length > doDaiToiDa ? ly.Substring(0, doDaiToiDa) : ly;
            IngestState = TrangThaiNap.Loi;
            IngestSource = null;
            Sha256 = null;
        }

        // =====================================================================
        // TAO MAU
        // =====================================================================

        /// <summary>
        /// Anh trong ZIP: he thong tu dat khoa "{projectId}/{sampleId}{duoi}" — khong
        /// dung ten file cua nguoi dung (co the chua "../" hay ky tu la). Xu ly ngay trong request → ready.
        /// </summary>
        public static Sample TaoAnhTrongZip(
            Guid projectId,
            string originalName,
            string contentType,
            string extension,
            long sizeBytes,
            string sha256,
            SampleMetadata metadata,
            DateTimeOffset luc,
            QuyDinhDuLieu quyDinh)
        {
            Sample s = TaoKhung(projectId, Modalities.Image, originalName, metadata, luc, quyDinh);
            s.Sha256 = sha256;
            s.StorageKey = projectId.ToString() + "/" + s.Id.ToString() + extension;
            s.ContentType = contentType;
            s.SizeBytes = sizeBytes;
            return s;
        }

        /// <summary>File da nam san trong MinIO va da doc xong (doan cat cua file dai, du lieu seed) → ready.</summary>
        public static Sample TaoTuFile(
            Guid projectId,
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
            KiemKhoaFile(projectId, modality, storageKey);

            Sample s = TaoKhung(projectId, modality, originalName, metadata, luc, quyDinh);
            s.Sha256 = sha256;
            s.StorageKey = storageKey;
            s.ContentType = contentType;
            s.SizeBytes = sizeBytes;
            if (modality == Modalities.Video)
            {
                // Cho media-svc chuan hoa xong (video.prepared) moi cho publish.
                s.MediaState = TrangThaiMedia.DangChuanBi;
            }

            return s;
        }

        /// <summary>
        /// File client DA upload thang len MinIO (link ky san) nhung chua doc: tao mau "cho xu ly". "goiY" la
        /// thong tin client khai (durationSec, width...) — chi dung khi may khong do duoc (khong co ffprobe).
        /// </summary>
        public static Sample TaoChoNap(
            Guid projectId,
            string modality,
            string storageKey,
            string originalName,
            SampleMetadata goiY,
            DateTimeOffset luc,
            QuyDinhDuLieu quyDinh)
        {
            KiemKhoaFile(projectId, modality, storageKey);

            Sample s = TaoKhung(projectId, modality, originalName, goiY, luc, quyDinh);
            s.StorageKey = storageKey;
            s.IngestState = TrangThaiNap.ChoXuLy;
            return s;
        }

        /// <summary>
        /// File o NGUON NGOAI (URL / Drive / S3 da ky san): he thong tu dat khoa "{projectId}/import/{sampleId}{duoi}",
        /// worker tai tu <paramref name="nguon" /> vao do roi xu ly nhu file da upload.
        /// </summary>
        public static Sample TaoChoTai(
            Guid projectId,
            string modality,
            string originalName,
            string extension,
            string nguon,
            DateTimeOffset luc,
            QuyDinhDuLieu quyDinh)
        {
            if (!Modalities.LaFile(modality))
            {
                throw new InvalidValueException("loai_du_lieu_khong_phai_file", "Dữ liệu " + modality + " không lưu thành file.");
            }

            if (string.IsNullOrWhiteSpace(nguon))
            {
                throw new ArgumentException("Thieu nguon tai.", nameof(nguon));
            }

            Sample s = TaoKhung(projectId, modality, originalName, SampleMetadata.Rong, luc, quyDinh);
            s.StorageKey = projectId.ToString() + "/import/" + s.Id.ToString() + extension;
            s.IngestSource = nguon;
            s.IngestState = TrangThaiNap.ChoXuLy;
            return s;
        }

        /// <summary>Text / pair / list: noi dung nam ngay trong database → ready.</summary>
        public static Sample TaoTuNoiDung(
            Guid projectId,
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
                throw new InvalidValueException("loai_du_lieu_la_file", "Dữ liệu " + modality + " phải là file.");
            }

            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            Sample s = TaoKhung(projectId, modality, originalName, metadata, luc, quyDinh);
            s.Sha256 = sha256;
            s.Content = content;
            return s;
        }

        private static void KiemKhoaFile(Guid projectId, string modality, string storageKey)
        {
            if (!Modalities.LaFile(modality))
            {
                throw new InvalidValueException("loai_du_lieu_khong_phai_file", "Dữ liệu " + modality + " không lưu thành file.");
            }

            if (string.IsNullOrWhiteSpace(storageKey) || !storageKey.StartsWith(projectId.ToString() + "/", StringComparison.Ordinal))
            {
                // Chan dung file cua du an khac (BOLA).
                throw new InvalidValueException("file_khong_thuoc_du_an", "File không thuộc dự án này.");
            }
        }

        private static Sample TaoKhung(
            Guid projectId, string modality, string originalName, SampleMetadata metadata, DateTimeOffset luc, QuyDinhDuLieu quyDinh)
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
            s.Modality = modality;
            s.OriginalName = ten;
            s.Metadata = metadata.ToRawJson();
            s.CreatedAt = luc;
            return s;
        }
    }

    /// <summary>Gia tri cua Sample.IngestState.</summary>
    public static class TrangThaiNap
    {
        /// <summary>Da co dong trong database (file da len MinIO / da co link nguon), dang xep hang cho worker.</summary>
        public const string ChoXuLy = "queued";

        /// <summary>Worker dang tai / doc file, do thong tin.</summary>
        public const string DangXuLy = "processing";

        public const string SanSang = "ready";

        public const string Loi = "failed";
    }

    /// <summary>Gia tri cua Sample.MediaState.</summary>
    public static class TrangThaiMedia
    {
        public const string DangChuanBi = "preparing";

        public const string SanSang = "ready";

        public const string Loi = "failed";
    }
}
