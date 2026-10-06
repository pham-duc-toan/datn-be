using System;
using Crowd.Labeling;

namespace Crowd.Annotation.Domain.Projects
{
    /// <summary>
    /// "Dieu khoan" cua du an ma annotation-svc can: chu du an, don gia, phi da
    /// chot, tap nhan. Dung tu project.published — BAT BIEN sau publish, nen
    /// khong co chuyen event den sai thu tu ghi de nhau.
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
    }
}
