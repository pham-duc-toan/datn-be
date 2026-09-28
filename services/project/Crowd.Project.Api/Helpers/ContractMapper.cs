using System;
using Crowd.Contracts.Project;
using Crowd.Project.Domain.Gold;
using Crowd.Project.Domain.Members;
using Crowd.Project.Domain.Projects;

namespace Crowd.Project.Api.Helpers
{
    /// <summary>
    /// Doi enum cua Domain sang enum cua hop dong event.
    ///
    /// Vi sao co HAI bo enum giong nhau: Domain khong duoc tham chieu gi ca (ke
    /// ca Contracts) de giu thuan nghiep vu. Doi lai phai map o day — va switch
    /// tuong minh nem loi khi gap gia tri moi chua map, thay vi ep kieu so
    /// (int) am tham lech khi hai ben chen gia tri o hai vi tri khac nhau.
    /// </summary>
    public static class ContractMapper
    {
        public static ProjectTaskType ToContract(TaskType t)
        {
            switch (t)
            {
                case TaskType.ImageClassification: return ProjectTaskType.ImageClassification;
                case TaskType.BoundingBox: return ProjectTaskType.BoundingBox;
                case TaskType.Segmentation: return ProjectTaskType.Segmentation;
                case TaskType.TextClassification: return ProjectTaskType.TextClassification;
                case TaskType.NamedEntityRecognition: return ProjectTaskType.NamedEntityRecognition;
                case TaskType.Sentiment: return ProjectTaskType.Sentiment;
                case TaskType.PairwiseComparison: return ProjectTaskType.PairwiseComparison;
                case TaskType.AudioTranscription: return ProjectTaskType.AudioTranscription;
                case TaskType.VideoTracking: return ProjectTaskType.VideoTracking;
                case TaskType.LlmResponseComparison: return ProjectTaskType.LlmResponseComparison;
                default: throw new ArgumentOutOfRangeException(nameof(t), t, "TaskType chua map sang hop dong.");
            }
        }

        public static ProjectMemberRole ToContract(MemberRole r)
        {
            switch (r)
            {
                case MemberRole.Owner: return ProjectMemberRole.Owner;
                case MemberRole.Labeler: return ProjectMemberRole.Labeler;
                case MemberRole.Reviewer: return ProjectMemberRole.Reviewer;
                default: throw new ArgumentOutOfRangeException(nameof(r), r, "MemberRole chua map sang hop dong.");
            }
        }

        public static Crowd.Contracts.Project.GoldPurpose ToContract(Crowd.Project.Domain.Gold.GoldPurpose p)
        {
            switch (p)
            {
                case Crowd.Project.Domain.Gold.GoldPurpose.EntranceTest:
                    return Crowd.Contracts.Project.GoldPurpose.EntranceTest;
                case Crowd.Project.Domain.Gold.GoldPurpose.QualityCheck:
                    return Crowd.Contracts.Project.GoldPurpose.QualityCheck;
                default:
                    throw new ArgumentOutOfRangeException(nameof(p), p, "GoldPurpose chua map sang hop dong.");
            }
        }
    }
}
