using System;
using Crowd.Labeling;

namespace Crowd.Annotation.Domain.Annotations
{
    /// <summary>
    /// Ket qua dong thuan cua MOT task do quality-svc tinh (consensus.reached). Chi la
    /// GOI Y cho nguoi duyet — khong tu duyet, khong tu chi tien.
    ///
    /// Mot task co the nhan nhieu lan (redundancy tang roi tinh lai): ban co
    /// occurredAt moi hon ghi de, ban cu den tre bi bo qua (VD-D-05).
    /// </summary>
    public sealed class TaskConsensus
    {
        private TaskConsensus()
        {
            Status = string.Empty;
        }

        public Guid TaskId { get; private set; }

        public Guid ProjectId { get; private set; }

        public Guid SampleId { get; private set; }

        /// <summary>"agreed" | "disputed" | "notApplicable" — giu nguyen chuoi tu hop dong.</summary>
        public string Status { get; private set; }

        /// <summary>Ket qua chot theo tung cong cu gop duoc. null khi notApplicable.</summary>
        public RawJson? Final { get; private set; }

        /// <summary>occurredAt cua event da ap dung.</summary>
        public DateTimeOffset DecidedAt { get; private set; }

        public static TaskConsensus Tao(Guid taskId, Guid projectId, Guid sampleId)
        {
            TaskConsensus c = new TaskConsensus();
            c.TaskId = taskId;
            c.ProjectId = projectId;
            c.SampleId = sampleId;
            c.DecidedAt = DateTimeOffset.MinValue;
            return c;
        }

        /// <summary>Ap dung ket qua moi. false = event cu hon ban dang co (bo qua).</summary>
        public bool ApDung(string status, RawJson? final, DateTimeOffset occurredAt)
        {
            if (occurredAt < DecidedAt)
            {
                return false;
            }

            Status = status;
            Final = final;
            DecidedAt = occurredAt;
            return true;
        }
    }
}
