using System;
using Crowd.Labeling;

namespace Crowd.Annotation.Domain.Projects
{
    /// <summary>
    /// Ban sao du an ma annotation-svc can: chu du an, loai du lieu, tap nhan (de gop ket qua va xuat file).
    /// Dung tu project.published — BAT BIEN sau publish.
    ///
    /// Du an lam trung tam (docs 3.18): tien KHONG con di qua annotation-svc (ledger tra thuong theo ca bai),
    /// nen o day khong giu don gia / phi, khong con "dong so" chan duyet. Chi danh dau du an da ket thuc.
    /// </summary>
    public sealed class ProjectTerms
    {
        // Tap nhan dang chuan (Crowd.Labeling) — cot jsonb. Doc thanh LabelSchema mot lan roi giu lai.
        private string _labelSchemaJson;
        private LabelSchema? _labelSchema;

        private ProjectTerms()
        {
            Modality = string.Empty;
            _labelSchemaJson = string.Empty;
        }

        public Guid ProjectId { get; private set; }

        public Guid OwnerId { get; private set; }

        /// <summary>Loai du lieu (image / text / audio / video / pair / list).</summary>
        public string Modality { get; private set; }

        /// <summary>Luc du an ket thuc (project.completed / project.cancelled). null = dang mo.</summary>
        public DateTimeOffset? ClosedAt { get; private set; }

        /// <summary>Da nhan project.completed / project.cancelled.</summary>
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

        public static ProjectTerms Tao(Guid projectId, Guid ownerId, LabelSchema labelSchema)
        {
            if (labelSchema == null)
            {
                throw new ArgumentNullException(nameof(labelSchema));
            }

            ProjectTerms t = new ProjectTerms();
            t.ProjectId = projectId;
            t.OwnerId = ownerId;
            t.Modality = labelSchema.Modality;
            t._labelSchemaJson = labelSchema.ToRawJson().Json;
            t._labelSchema = labelSchema;
            return t;
        }

        /// <summary>project.completed / project.cancelled: ket thuc (idempotent — giu moc dau).</summary>
        public void KetThuc(DateTimeOffset luc)
        {
            if (!ClosedAt.HasValue)
            {
                ClosedAt = luc;
            }

            IsFinal = true;
        }
    }
}
