using System;
using Crowd.Annotation.Domain.Common;
using Crowd.Labeling;

namespace Crowd.Annotation.Domain.Projects
{
    /// <summary>
    /// "Dieu khoan" cua du an ma annotation-svc can: chu du an, don gia, phi da
    /// chot, tap nhan. Dung tu project.published — BAT BIEN sau publish, nen
    /// khong co chuyen event den sai thu tu ghi de nhau.
    ///
    /// Rieng trang thai DONG SO thay doi duoc: project-svc goi "dong so" truoc khi
    /// hoan thanh / huy du an (ky quy sap tra ve doanh nghiep). Da dong thi khong
    /// duyet, khieu nai, phan xu them duoc — tien khong con de tra.
    ///
    /// annotation-svc KHONG hoi project-svc "don gia bao nhieu" luc duyet; no doc
    /// ban sao nay (luat 1, muc 4 docs).
    /// </summary>
    public sealed class ProjectTerms
    {
        // Tap nhan dang chuan (Crowd.Labeling) — cot jsonb. Doc thanh LabelSchema
        // mot lan roi giu lai.
        private string _labelSchemaJson;
        private LabelSchema? _labelSchema;

        private ProjectTerms()
        {
            Modality = string.Empty;
            _labelSchemaJson = string.Empty;
        }

        public Guid ProjectId { get; private set; }

        public Guid OwnerId { get; private set; }

        /// <summary>Thu lao labeler moi nhan duoc duyet.</summary>
        public long UnitPriceVnd { get; private set; }

        /// <summary>Phi nen tang moi nhan, da chot luc publish (VD-M-15).</summary>
        public long PlatformFeeVnd { get; private set; }

        /// <summary>Loai du lieu (image / text / audio / video / pair).</summary>
        public string Modality { get; private set; }

        /// <summary>
        /// Luc dong so (project-svc goi truoc khi hoan thanh / huy). null = dang mo.
        /// </summary>
        public DateTimeOffset? ClosedAt { get; private set; }

        /// <summary>
        /// Da nhan project.completed / project.cancelled: dong VINH VIEN, khong event
        /// project.resumed nao mo lai duoc.
        /// </summary>
        public bool IsFinal { get; private set; }

        public bool DaDong
        {
            get { return ClosedAt.HasValue; }
        }

        /// <summary>Tap nhan — de gop ket qua theo tung cong cu va xuat file (COCO...).</summary>
        public LabelSchema LabelSchema
        {
            get
            {
                if (_labelSchema == null)
                {
                    _labelSchema = LabelSchema.Doc(_labelSchemaJson);
                }

                return _labelSchema;
            }
        }

        public static ProjectTerms Tao(
            Guid projectId,
            Guid ownerId,
            long unitPriceVnd,
            long platformFeeVnd,
            LabelSchema labelSchema)
        {
            if (labelSchema == null)
            {
                throw new ArgumentNullException(nameof(labelSchema));
            }

            ProjectTerms t = new ProjectTerms();
            t.ProjectId = projectId;
            t.OwnerId = ownerId;
            t.UnitPriceVnd = unitPriceVnd;
            t.PlatformFeeVnd = platformFeeVnd;
            t.Modality = labelSchema.Modality;
            t._labelSchemaJson = labelSchema.ToRawJson().Json;
            t._labelSchema = labelSchema;
            return t;
        }

        /// <summary>Dong so (idempotent — da dong thi giu moc cu).</summary>
        public void DongSo(DateTimeOffset luc)
        {
            if (!ClosedAt.HasValue)
            {
                ClosedAt = luc;
            }
        }

        /// <summary>
        /// project.resumed: doanh nghiep chay tiep sau lan dong so ma project-svc chua
        /// kip chot (vd loi giua chung). Chi mo lai khi event MOI HON luc dong so va du
        /// an chua ket thuc han — event cu giao tre khong mo nham du an da dong.
        /// Tra ve true neu da mo lai.
        /// </summary>
        public bool MoLai(DateTimeOffset luc)
        {
            if (IsFinal || !ClosedAt.HasValue || luc <= ClosedAt.Value)
            {
                return false;
            }

            ClosedAt = null;
            return true;
        }

        /// <summary>project.completed / project.cancelled: dong vinh vien.</summary>
        public void KetThuc(DateTimeOffset luc)
        {
            DongSo(luc);
            IsFinal = true;
        }

        /// <summary>Nem 409 neu du an da dong so — goi truoc moi thao tac lam doi tien.</summary>
        public void KiemConMo()
        {
            if (ClosedAt.HasValue)
            {
                throw new RuleViolationException(
                    "du_an_da_ket_thuc",
                    "Du an da hoan thanh hoac da huy — khong duyet, khieu nai hay phan xu them duoc.");
            }
        }
    }
}
