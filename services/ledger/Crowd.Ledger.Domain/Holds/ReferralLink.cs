using System;

namespace Crowd.Ledger.Domain.Holds
{
    /// <summary>
    /// Ban sao quan he gioi thieu (referral.registered tu link-svc). Khoa chinh = nguoi duoc
    /// moi: moi nguoi chi co mot nguoi gioi thieu.
    /// </summary>
    public sealed class ReferralLink
    {
        private ReferralLink()
        {
        }

        public Guid ReferredId { get; private set; }

        public Guid ReferrerId { get; private set; }

        public DateTimeOffset RegisteredAt { get; private set; }

        public static ReferralLink Tao(Guid referrerId, Guid referredId, DateTimeOffset luc)
        {
            ReferralLink r = new ReferralLink();
            r.ReferrerId = referrerId;
            r.ReferredId = referredId;
            r.RegisteredAt = luc;
            return r;
        }
    }
}
