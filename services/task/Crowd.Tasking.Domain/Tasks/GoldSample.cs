using System;
using Crowd.Labeling;

namespace Crowd.Tasking.Domain.Tasks
{
    /// <summary>
    /// Ban sao cau hoi vang cua du an, dung tu gold_set.updated. Hien dung de
    /// LOAI mau vang khoi pool task tra tien; P3 se tron cau vang QualityCheck vao
    /// task that de giam sat (FQ-04) — dap an giu san o day cho luc do.
    /// </summary>
    public sealed class GoldSample
    {
        // Dap an o dinh dang nhan chung — ba cot, giong project-svc.
        private string _expectedTaskType;
        private int _expectedSchemaVersion;
        private string _expectedPayloadJson;

        private GoldSample()
        {
            Purpose = string.Empty;
            _expectedTaskType = string.Empty;
            _expectedPayloadJson = string.Empty;
        }

        public Guid ProjectId { get; private set; }

        public Guid SampleId { get; private set; }

        /// <summary>"entranceTest" | "qualityCheck" — giu nguyen chuoi tu hop dong.</summary>
        public string Purpose { get; private set; }

        public LabelPayload ExpectedPayload
        {
            get { return LabelPayload.Tao(_expectedTaskType, _expectedSchemaVersion, _expectedPayloadJson); }
        }

        public static GoldSample Tao(Guid projectId, Guid sampleId, string purpose, LabelPayload dapAn)
        {
            if (dapAn == null)
            {
                throw new ArgumentNullException(nameof(dapAn));
            }

            GoldSample g = new GoldSample();
            g.ProjectId = projectId;
            g.SampleId = sampleId;
            g.Purpose = purpose;
            g._expectedTaskType = dapAn.TaskType;
            g._expectedSchemaVersion = dapAn.SchemaVersion;
            g._expectedPayloadJson = dapAn.DataJson;
            return g;
        }
    }
}
