using System;
using System.Collections.Generic;

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
        private List<string> _labelClasses;

        private ProjectTerms()
        {
            _labelClasses = new List<string>();
        }

        public Guid ProjectId { get; private set; }

        public Guid OwnerId { get; private set; }

        /// <summary>Thu lao labeler moi nhan duoc duyet.</summary>
        public long UnitPriceVnd { get; private set; }

        /// <summary>Phi nen tang moi nhan, da chot luc publish (VD-M-15).</summary>
        public long PlatformFeeVnd { get; private set; }

        public bool AllowMultiple { get; private set; }

        public IReadOnlyList<string> LabelClasses
        {
            get { return _labelClasses; }
        }

        public static ProjectTerms Tao(
            Guid projectId,
            Guid ownerId,
            long unitPriceVnd,
            long platformFeeVnd,
            bool allowMultiple,
            IReadOnlyList<string> labelClasses)
        {
            if (labelClasses == null)
            {
                throw new ArgumentNullException(nameof(labelClasses));
            }

            ProjectTerms t = new ProjectTerms();
            t.ProjectId = projectId;
            t.OwnerId = ownerId;
            t.UnitPriceVnd = unitPriceVnd;
            t.PlatformFeeVnd = platformFeeVnd;
            t.AllowMultiple = allowMultiple;
            t._labelClasses = new List<string>(labelClasses);
            return t;
        }
    }
}
