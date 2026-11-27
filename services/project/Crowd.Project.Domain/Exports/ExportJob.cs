using System;
using Crowd.Project.Domain.Common;

namespace Crowd.Project.Domain.Exports
{
    /// <summary>
    /// MOT LAN TAO FILE KET QUA (kien truc 3.41). Xuat ket qua khong con lam trong request: doanh nghiep bam tao →
    /// dong nay "queued" + event export.requested cung transaction (outbox) → 202. Consumer gop ket qua, dung file,
    /// luu MinIO ("{projectId}/exports/{jobId}{duoi}") roi chot:
    ///
    ///   queued → processing → ready | failed
    ///
    /// File xong thi tai qua API (GET .../exports/{id}/file, chi chu du an) — khong phat link MinIO. Ket qua chua doi
    /// (cung dau van tay) thi dung lai file cua lan truoc thay vi luu ban moi.
    ///
    /// Qua setting export.job_timeout ma chua xong (consumer that bai het so lan thu, message nam trong DLQ) thi API
    /// bao failed — doanh nghiep bam tao lai, khong ai phai don tay.
    /// </summary>
    public sealed class ExportJob
    {
        public const int DoDaiLoiToiDa = 1000;

        private ExportJob()
        {
            Format = string.Empty;
            State = TrangThaiXuat.ChoXuLy;
        }

        public Guid Id { get; private set; }

        public Guid ProjectId { get; private set; }

        /// <summary>Dinh dang da chuan hoa (chu thuong): json, csv, coco, yolo...</summary>
        public string Format { get; private set; }

        /// <summary>Nguoi bam tao (chu du an / admin).</summary>
        public Guid? RequestedBy { get; private set; }

        /// <summary>TrangThaiXuat: queued | processing | ready | failed.</summary>
        public string State { get; private set; }

        /// <summary>Khoa file trong kho khi ready. Hai lan xuat cung ket qua co the dung chung mot khoa.</summary>
        public string? StorageKey { get; private set; }

        /// <summary>Ten goi y khi tai ve: "ket-qua-{projectId}.yolo.zip".</summary>
        public string? FileName { get; private set; }

        public string? ContentType { get; private set; }

        public long? SizeBytes { get; private set; }

        /// <summary>SHA-256 cua ket qua gop (khong phai cua file — file ZIP mang moc thoi gian nen moi lan mot khac).</summary>
        public string? ResultFingerprint { get; private set; }

        /// <summary>So mau co ket qua trong file.</summary>
        public int? SampleCount { get; private set; }

        /// <summary>Ly do khi failed (ngon ngu nguoi dung: "Dự án chưa có tập nhãn.", "coco chỉ xuất cho dự án ảnh...").</summary>
        public string? Error { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public DateTimeOffset? StartedAt { get; private set; }

        public DateTimeOffset? FinishedAt { get; private set; }

        public static ExportJob Tao(Guid projectId, string dinhDang, Guid? nguoiYeuCau, DateTimeOffset luc)
        {
            if (string.IsNullOrWhiteSpace(dinhDang) || dinhDang.Length > 30)
            {
                throw new ArgumentException("Thieu dinh dang.", nameof(dinhDang));
            }

            ExportJob j = new ExportJob();
            j.Id = Guid.CreateVersion7();
            j.ProjectId = projectId;
            j.Format = dinhDang;
            j.RequestedBy = nguoiYeuCau;
            j.State = TrangThaiXuat.ChoXuLy;
            j.CreatedAt = luc;
            return j;
        }

        public bool DangCho()
        {
            return State == TrangThaiXuat.ChoXuLy || State == TrangThaiXuat.DangXuLy;
        }

        public bool DaXong()
        {
            return State == TrangThaiXuat.SanSang || State == TrangThaiXuat.Loi;
        }

        /// <summary>Con dang cho nhung qua han — consumer bo cuoc (DLQ) hoac chet ma khong ai lam lai.</summary>
        public bool QuaHan(DateTimeOffset bayGio, TimeSpan han)
        {
            return DangCho() && CreatedAt + han < bayGio;
        }

        /// <summary>Trang thai cho API: dang cho qua han thi coi nhu failed (khong ghi DB — doc la du).</summary>
        public string TrangThaiHienThi(DateTimeOffset bayGio, TimeSpan han)
        {
            return QuaHan(bayGio, han) ? TrangThaiXuat.Loi : State;
        }

        /// <summary>Consumer nhan viec. Giao lai (dang processing) thi giu nguyen — lam lai tu dau la vo hai.</summary>
        public void BatDau(DateTimeOffset luc)
        {
            if (DaXong())
            {
                throw new RuleViolationException("xuat_da_xong", "Lần xuất này đã kết thúc (" + State + ").");
            }

            if (State == TrangThaiXuat.ChoXuLy)
            {
                State = TrangThaiXuat.DangXuLy;
                StartedAt = luc;
            }
        }

        public void HoanTat(string storageKey, string fileName, string contentType, long sizeBytes, string fingerprint, int soMau, DateTimeOffset luc)
        {
            if (State != TrangThaiXuat.DangXuLy)
            {
                throw new RuleViolationException("xuat_khong_dang_xu_ly", "Lần xuất này không đang xử lý (" + State + ").");
            }

            if (string.IsNullOrWhiteSpace(storageKey) || !storageKey.StartsWith(ProjectId.ToString() + "/", StringComparison.Ordinal))
            {
                throw new ArgumentException("Khoa file phai nam trong thu muc du an.", nameof(storageKey));
            }

            StorageKey = storageKey;
            FileName = fileName;
            ContentType = contentType;
            SizeBytes = sizeBytes;
            ResultFingerprint = fingerprint;
            SampleCount = soMau;
            Error = null;
            State = TrangThaiXuat.SanSang;
            FinishedAt = luc;
        }

        public void Loi(string lyDo, DateTimeOffset luc)
        {
            if (DaXong())
            {
                return;
            }

            string ly = string.IsNullOrWhiteSpace(lyDo) ? "Không tạo được file." : lyDo.Trim();
            Error = ly.Length > DoDaiLoiToiDa ? ly.Substring(0, DoDaiLoiToiDa) : ly;
            State = TrangThaiXuat.Loi;
            StartedAt = StartedAt ?? luc;
            FinishedAt = luc;
        }

        /// <summary>Khoa file moi cho lan xuat nay (duoi file theo dinh dang, vd ".yolo.zip").</summary>
        public string KhoaFileMoi(string duoi)
        {
            return ProjectId.ToString() + "/exports/" + Id.ToString() + duoi;
        }
    }

    /// <summary>Gia tri cua ExportJob.State.</summary>
    public static class TrangThaiXuat
    {
        public const string ChoXuLy = "queued";

        public const string DangXuLy = "processing";

        public const string SanSang = "ready";

        public const string Loi = "failed";
    }
}
