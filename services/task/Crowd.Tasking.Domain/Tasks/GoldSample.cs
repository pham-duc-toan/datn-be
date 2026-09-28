using System;
using System.Collections.Generic;

namespace Crowd.Tasking.Domain.Tasks
{
    /// <summary>
    /// Ban sao cau hoi vang cua du an, dung tu gold_set.updated. Hien dung de
    /// LOAI mau vang khoi pool task tra tien; P3 se tron cau vang QualityCheck vao
    /// task that de giam sat (FQ-04) — dap an giu san o day cho luc do.
    /// </summary>
    public sealed class GoldSample
    {
        private List<string> _expectedLabels;

        private GoldSample()
        {
            Purpose = string.Empty;
            _expectedLabels = new List<string>();
        }

        public Guid ProjectId { get; private set; }

        public Guid SampleId { get; private set; }

        /// <summary>"entranceTest" | "qualityCheck" — giu nguyen chuoi tu hop dong.</summary>
        public string Purpose { get; private set; }

        public IReadOnlyList<string> ExpectedLabels
        {
            get { return _expectedLabels; }
        }

        public static GoldSample Tao(Guid projectId, Guid sampleId, string purpose, IReadOnlyList<string> expectedLabels)
        {
            GoldSample g = new GoldSample();
            g.ProjectId = projectId;
            g.SampleId = sampleId;
            g.Purpose = purpose;
            g._expectedLabels = new List<string>(expectedLabels);
            return g;
        }
    }
}
