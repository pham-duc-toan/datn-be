using System;
using System.Collections.Generic;
using Crowd.Project.Domain.Common;
using Crowd.Labeling;

namespace Crowd.Project.Domain.Annotations
{
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
    /// AGGREGATE ROOT: mot nhan do mot nguoi gan cho mot mau — mot cau trong BAI LAM ca du an (docs 3.18).
    ///
    /// Vong doi (khong con duyet tung nhan):
    ///
    ///   Submitted ──project.graded: bai DAT──► Accepted   (vao ket qua)
    ///       │
    ///       └──project.graded: bai TRUOT──► Excluded ──khieu nai ca bai duoc chap nhan──► Accepted
    ///
    /// Tien KHONG di qua annotation-svc nua: ledger tra thuong theo ca bai (project.graded /
    /// attempt.appeal_resolved). Annotation chi lo LUU nhan va CHOT nhan nao vao ket qua / file xuat.
    ///
    /// Lich su doi trang thai KHONG ghi theo tung nhan: moi lan doi deu xay ra theo CA BAI (nop, cham dat /
    /// truot, khieu nai) nen ghi mot dong attempt_history cho ca bai thay vi N dong cho N nhan.
    ///
    /// Bang annotations phan vung HASH(project_id) 64 phan: moi truy van (nop, cham, ket qua, xuat file) deu
    /// theo du an nen chi cham MOT phan vung. Ten la LabelAnnotation vi "Annotation" trung namespace cu.
    /// </summary>
    public sealed class LabelAnnotation
    {
        // Noi dung nhan o dinh dang chung (Crowd.Labeling) — ba cot task_type,
        // schema_version, payload jsonb. Moi loai nhan mot hinh dang JSON, bang
        // khong doi khi them loai moi.
        private string _payloadTaskType;
        private int _payloadSchemaVersion;
        private string _payloadJson;

        private LabelAnnotation()
        {
            _payloadTaskType = string.Empty;
            _payloadJson = string.Empty;
        }

        public Guid Id { get; private set; }

        /// <summary>
        /// Bai lam sinh ra nhan nay (cong link: id tat dinh (phien, mau)). UNIQUE (attempt, mau): bai nop giao
        /// lai khong thanh hai nhan.
        /// </summary>
        public Guid AttemptId { get; private set; }

        public Guid ProjectId { get; private set; }

        /// <summary>Mau duoc gan — khoa file / noi dung / metadata nam o AnnotationSample (mot ban moi mau).</summary>
        public Guid SampleId { get; private set; }

        /// <summary>null = khach vang lai o cong link.</summary>
        public Guid? LabelerId { get; private set; }

        /// <summary>Noi dung nhan. Dung lai tu ba cot moi lan doc — LabelPayload kiem lai dinh dang.</summary>
        public LabelPayload Payload
        {
            get { return LabelPayload.TuLuuTru(_payloadTaskType, _payloadSchemaVersion, _payloadJson); }
        }

        public LabelSource Source { get; private set; }

        public AnnotationStatus Status { get; private set; }

        public DateTimeOffset SubmittedAt { get; private set; }

        /// <summary>Luc chot vao / loai khoi ket qua (cham, khieu nai).</summary>
        public DateTimeOffset? DecidedAt { get; private set; }

        /// <summary>Mot cau trong bai DA NOP (attempt.submitted) — cho cham.</summary>
        public static LabelAnnotation TaoTuBaiNop(
            Guid attemptId,
            Guid projectId,
            Guid sampleId,
            Guid labelerId,
            LabelPayload nhan,
            DateTimeOffset submittedAt)
        {
            // LabelPayload luon hop le dinh dang (chi tao duoc qua LabelPayload.Tao), nen o day chi can kiem
            // co hay khong.
            if (nhan == null)
            {
                throw new InvalidValueException("nhan_rong", "Nhãn không được rỗng.");
            }

            LabelAnnotation a = new LabelAnnotation();
            a.Id = Guid.CreateVersion7();
            a.AttemptId = attemptId;
            a.ProjectId = projectId;
            a.SampleId = sampleId;
            a.LabelerId = labelerId;
            a._payloadTaskType = nhan.TaskType;
            a._payloadSchemaVersion = nhan.SchemaVersion;
            a._payloadJson = nhan.DataJson;
            a.Source = LabelSource.Professional;
            a.Status = AnnotationStatus.Submitted;
            a.SubmittedAt = submittedAt;
            return a;
        }

        /// <summary>
        /// Nhan cua khach vang lai qua trang vuot link (gate.solved): da qua cau vang, KHONG co labeler (nguoi
        /// chia se link da duoc tra theo luot), khong thuoc bai lam nao → vao ket qua ngay (Accepted).
        /// attemptId la id tat dinh (phien, mau) — event giao lai khong tao nhan thu hai.
        /// </summary>
        public static LabelAnnotation TaoTuCongLink(
            Guid attemptId,
            Guid projectId,
            Guid sampleId,
            LabelPayload nhan,
            DateTimeOffset submittedAt)
        {
            if (nhan == null)
            {
                throw new InvalidValueException("nhan_rong", "Nhãn không được rỗng.");
            }

            LabelAnnotation a = new LabelAnnotation();
            a.Id = Guid.CreateVersion7();
            a.AttemptId = attemptId;
            a.ProjectId = projectId;
            a.SampleId = sampleId;
            a.LabelerId = null;
            a._payloadTaskType = nhan.TaskType;
            a._payloadSchemaVersion = nhan.SchemaVersion;
            a._payloadJson = nhan.DataJson;
            a.Source = LabelSource.LinkGateway;
            a.Status = AnnotationStatus.Accepted;
            a.SubmittedAt = submittedAt;
            a.DecidedAt = submittedAt;
            return a;
        }

        /// <summary>
        /// project.graded: bai dat → Accepted, bai truot → Excluded. Chi tu Submitted (giao lai / da chot →
        /// khong doi). Tra ve true neu vua doi.
        /// </summary>
        public bool ChotTheoKetQua(bool dat, DateTimeOffset luc)
        {
            if (Status != AnnotationStatus.Submitted)
            {
                return false;
            }

            Status = dat ? AnnotationStatus.Accepted : AnnotationStatus.Excluded;
            DecidedAt = luc;
            return true;
        }

        /// <summary>Khieu nai ca bai duoc chap nhan: Excluded → Accepted (vao ket qua).</summary>
        public bool ChapNhanSauKhieuNai(DateTimeOffset luc)
        {
            if (Status != AnnotationStatus.Excluded)
            {
                return false;
            }

            Status = AnnotationStatus.Accepted;
            DecidedAt = luc;
            return true;
        }
    }
}
