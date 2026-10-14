using System;
using Crowd.Labeling;

namespace Crowd.Annotation.Domain.Annotations
{
    /// <summary>
    /// Thong tin MAU ma cac nhan tro toi (khoa file / noi dung van ban / metadata) — luu MOT lan cho moi
    /// mau. Truoc day moi nhan chep rieng mot ban: mau van ban 100.000 ky tu, 3 nguoi gan = 3 ban giong het.
    /// Mau khong doi sau khi nap nen khong can cap nhat (ghi lan dau, lan sau bo qua).
    /// </summary>
    public sealed class AnnotationSample
    {
        private AnnotationSample()
        {
            Metadata = SampleMetadata.Rong.ToRawJson();
        }

        public Guid SampleId { get; private set; }

        public Guid ProjectId { get; private set; }

        /// <summary>Khoa file trong kho — null voi du lieu khong phai file (text, pair).</summary>
        public string? StorageKey { get; private set; }

        /// <summary>Noi dung khi khong phai file: {"text"} hoac {"prompt","a","b"}.</summary>
        public RawJson? Content { get; private set; }

        public RawJson Metadata { get; private set; }

        public static AnnotationSample Tao(Guid sampleId, Guid projectId, string? storageKey, RawJson? content, RawJson metadata)
        {
            if (metadata == null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }

            AnnotationSample m = new AnnotationSample();
            m.SampleId = sampleId;
            m.ProjectId = projectId;
            m.StorageKey = storageKey;
            m.Content = content;
            m.Metadata = metadata;
            return m;
        }
    }
}
