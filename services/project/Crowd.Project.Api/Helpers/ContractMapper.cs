using System;
using Crowd.Contracts.Project;
using Crowd.Project.Domain.Gold;
using Crowd.Project.Domain.Members;

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
