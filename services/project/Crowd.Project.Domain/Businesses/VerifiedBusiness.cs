using System;

namespace Crowd.Project.Domain.Businesses
{
    /// <summary>
    /// BAN SAO read-only: doanh nghiep da duoc identity-svc xac minh (business.verified). Chi doanh
    /// nghiep co ban ghi o day moi publish duoc du an (FB-02) — project-svc khong hoi identity luc
    /// publish (luat 1, muc 4 docs).
    /// </summary>
    public sealed class VerifiedBusiness
    {
        private VerifiedBusiness()
        {
        }

        public Guid BusinessId { get; private set; }

        public DateTimeOffset VerifiedAt { get; private set; }

        public static VerifiedBusiness Tao(Guid businessId, DateTimeOffset verifiedAt)
        {
            VerifiedBusiness v = new VerifiedBusiness();
            v.BusinessId = businessId;
            v.VerifiedAt = verifiedAt;
            return v;
        }
    }
}
