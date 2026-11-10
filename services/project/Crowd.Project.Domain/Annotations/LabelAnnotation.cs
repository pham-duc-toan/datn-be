using System;
using System.Text.Json.Nodes;
using Crowd.Labeling;
using Crowd.Project.Domain.Attempts;
using Crowd.Project.Domain.Common;

namespace Crowd.Project.Domain.Annotations
{
    /// <summary>
    /// Trang thai cua mot nhan — KHONG luu trong bang annotations, SUY RA tu bai lam (xem LabelAnnotation.TrangThai).
    /// </summary>
    public enum AnnotationStatus
    {
        /// <summary>Nam trong mot bai DA NOP, cho du an ket thuc va cham (docs 3.18).</summary>
        Submitted,

        /// <summary>Vao KET QUA: bai cua labeler DAT (hoac khieu nai duoc chap nhan), hoac nhan cong link da qua cau vang.</summary>
        Accepted,

        /// <summary>Bai cua labeler TRUOT — khong vao ket qua (van luu de doi chieu / khieu nai).</summary>
        Excluded,
    }

    public enum LabelSource
    {
        Professional,
        LinkGateway,
        Collaborative,
    }

    /// <summary>
    /// MOT NHAN: cai nguoi dung chon cho MOT cau hoi (mau) trong MOT bai lam. Bang annotations chi giu dung
    /// nhung gi khong suy ra duoc tu cho khac:
    ///
    ///   project_id   khoa phan vung (moi truy van deu theo du an)
    ///   attempt_id   bai lam sinh ra nhan (cong link: id tat dinh (phien, mau) — khach khong co tai khoan)
    ///   sample_id    cau hoi
    ///   labeler_id   nguoi lam (null = khach vang lai o cong link)
    ///   source       kenh: Professional | LinkGateway | Collaborative
    ///   payload      cau tra loi (JSON theo ten cong cu)
    ///
    /// Khoa chinh (project_id, attempt_id, sample_id): moi bai mot nhan moi cau — bai nop / gate.solved giao lai
    /// khong sinh nhan thu hai.
    ///
    /// Nhung thu DA BO vi suy ra duoc:
    ///   task_type, schema_version → projects.modality + LabelPayload.PhienBanHienTai (tap nhan khoa tu luc roi Nhap,
    ///                               nen moi nhan cua mot du an cung mot dinh dang)
    ///   status                    → TrangThai(...) tu attempts.graded_at / passed / appeal_state
    ///   submitted_at, decided_at  → attempts.submitted_at / graded_at / appeal_resolved_at
    ///
    /// Vi khong con status tren tung nhan, cham bai va phan xu khieu nai chi sua MOT dong attempts, khong UPDATE
    /// hang loat nhan.
    ///
    /// Nhap (cau dang lam do) KHONG luu o backend: frontend giu trong localStorage, nop thi gui ca bai.
    ///
    /// Bang annotations phan vung HASH(project_id) 64 phan. Ten la LabelAnnotation vi "Annotation" trung namespace cu.
    /// </summary>
    public sealed class LabelAnnotation
    {
        private LabelAnnotation()
        {
            DataJson = string.Empty;
        }

        public Guid ProjectId { get; private set; }

        /// <summary>Bai lam sinh ra nhan nay (cong link: id tat dinh (phien, mau)).</summary>
        public Guid AttemptId { get; private set; }

        /// <summary>Cau hoi (mau) duoc gan.</summary>
        public Guid SampleId { get; private set; }

        /// <summary>null = khach vang lai o cong link.</summary>
        public Guid? LabelerId { get; private set; }

        public LabelSource Source { get; private set; }

        /// <summary>Cau tra loi da kiem va chuan hoa — phan DataJson cua LabelPayload.</summary>
        public string DataJson { get; private set; }

        /// <summary>
        /// Dung lai LabelPayload day du. Loai du lieu lay tu du an (projects.modality) vi bang khong luu task_type.
        /// </summary>
        public LabelPayload LayPayload(string modality)
        {
            return LabelPayload.TuLuuTru(modality, LabelPayload.PhienBanHienTai, DataJson);
        }

        /// <summary>Ket qua cua mot cong cu; null neu nhan khong co cong cu nay.</summary>
        public JsonNode? KetQua(string tenCongCu)
        {
            JsonObject? o = JsonNode.Parse(DataJson) as JsonObject;
            if (o == null)
            {
                return null;
            }

            JsonNode? n = o[tenCongCu];
            return n == null ? null : n.DeepClone();
        }

        /// <summary>Mot cau trong bai DA NOP — cho cham.</summary>
        public static LabelAnnotation TaoTuBaiNop(Guid attemptId, Guid projectId, Guid sampleId, Guid labelerId, LabelPayload nhan)
        {
            // LabelPayload luon hop le dinh dang (chi tao duoc qua LabelPayload.Tao), nen o day chi can kiem
            // co hay khong.
            if (nhan == null)
            {
                throw new InvalidValueException("nhan_rong", "Nhãn không được rỗng.");
            }

            LabelAnnotation a = new LabelAnnotation();
            a.AttemptId = attemptId;
            a.ProjectId = projectId;
            a.SampleId = sampleId;
            a.LabelerId = labelerId;
            a.DataJson = nhan.DataJson;
            a.Source = LabelSource.Professional;
            return a;
        }

        /// <summary>
        /// Nhan cua khach vang lai qua trang vuot link (gate.solved): da qua cau vang, KHONG co labeler, khong
        /// thuoc bai lam nao → luon vao ket qua. attemptId la id tat dinh (phien, mau).
        /// </summary>
        public static LabelAnnotation TaoTuCongLink(Guid attemptId, Guid projectId, Guid sampleId, LabelPayload nhan)
        {
            if (nhan == null)
            {
                throw new InvalidValueException("nhan_rong", "Nhãn không được rỗng.");
            }

            LabelAnnotation a = new LabelAnnotation();
            a.AttemptId = attemptId;
            a.ProjectId = projectId;
            a.SampleId = sampleId;
            a.LabelerId = null;
            a.DataJson = nhan.DataJson;
            a.Source = LabelSource.LinkGateway;
            return a;
        }

        /// <summary>
        /// Trang thai cua nhan, suy ra tu bai lam:
        ///
        ///   cong link                              → Accepted (gate chi gui nhan cua khach da qua cau vang)
        ///   bai chua cham (graded_at null)         → Submitted
        ///   bai dat HOAC khieu nai duoc chap nhan  → Accepted
        ///   con lai                                → Excluded
        ///
        /// Truy van SQL (AnnotationService, LamBaiQueries) viet LAI dung luat nay de Postgres loc duoc.
        /// </summary>
        public static AnnotationStatus TrangThai(LabelSource source, Attempt? bai)
        {
            if (source == LabelSource.LinkGateway)
            {
                return AnnotationStatus.Accepted;
            }

            if (bai == null || !bai.GradedAt.HasValue)
            {
                return AnnotationStatus.Submitted;
            }

            return bai.DatCuoiCung ? AnnotationStatus.Accepted : AnnotationStatus.Excluded;
        }
    }
}
