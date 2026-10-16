using System;

namespace Crowd.Link.Api.Entities
{
    /// <summary>Chien dich: gom link de xem thong ke chung (FS-06).</summary>
    public sealed class Campaign
    {
        public const int CotTen = 100;

        private Campaign()
        {
            Name = string.Empty;
        }

        public Guid Id { get; private set; }

        public Guid OwnerId { get; private set; }

        public string Name { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public static Campaign Tao(Guid ownerId, string name, DateTimeOffset luc)
        {
            Campaign c = new Campaign();
            c.Id = Guid.CreateVersion7();
            c.OwnerId = ownerId;
            c.Name = name;
            c.CreatedAt = luc;
            return c;
        }
    }

    /// <summary>
    /// API key cua sharer (FS-05, FS-02). Chi luu BAM sha256 — key goc chi hien mot lan
    /// luc tao. Moi nguoi mot key; tao lai thi key cu het hieu luc.
    /// </summary>
    public sealed class ApiKey
    {
        private ApiKey()
        {
            KeyHash = string.Empty;
            Prefix = string.Empty;
        }

        public Guid UserId { get; private set; }

        public string KeyHash { get; private set; }

        /// <summary>Vai ky tu dau de nguoi dung nhan ra key nao dang dung.</summary>
        public string Prefix { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public static ApiKey Tao(Guid userId, string keyHash, string prefix, DateTimeOffset luc)
        {
            ApiKey k = new ApiKey();
            k.UserId = userId;
            k.KeyHash = keyHash;
            k.Prefix = prefix;
            k.CreatedAt = luc;
            return k;
        }

        public void DoiKey(string keyHash, string prefix, DateTimeOffset luc)
        {
            KeyHash = keyHash;
            Prefix = prefix;
            CreatedAt = luc;
        }
    }

    /// <summary>Ten mien bi chan (admin quan ly). Chan ca ten mien con: chan "x.com" la chan "a.x.com".</summary>
    public sealed class BlockedDomain
    {
        private BlockedDomain()
        {
            Domain = string.Empty;
            Reason = string.Empty;
        }

        public string Domain { get; private set; }

        public string Reason { get; private set; }

        public Guid? CreatedBy { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public static BlockedDomain Tao(string domain, string reason, Guid? createdBy, DateTimeOffset luc)
        {
            BlockedDomain d = new BlockedDomain();
            d.Domain = domain;
            d.Reason = reason;
            d.CreatedBy = createdBy;
            d.CreatedAt = luc;
            return d;
        }
    }

    /// <summary>Bao cao vi pham tu trang vuot link. UNIQUE (link, ip) — mot IP chi bao cao mot lan.</summary>
    public sealed class LinkReport
    {
        public const int CotLyDo = 500;

        private LinkReport()
        {
            IpHash = string.Empty;
            Reason = string.Empty;
        }

        public Guid Id { get; private set; }

        public Guid LinkId { get; private set; }

        public string IpHash { get; private set; }

        public string Reason { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public static LinkReport Tao(Guid linkId, string ipHash, string reason, DateTimeOffset luc)
        {
            LinkReport r = new LinkReport();
            r.Id = Guid.CreateVersion7();
            r.LinkId = linkId;
            r.IpHash = ipHash;
            r.Reason = reason;
            r.CreatedAt = luc;
            return r;
        }
    }

    /// <summary>Ma gioi thieu cua mot nguoi (FS-08) — tao lan dau ho xem trang gioi thieu.</summary>
    public sealed class ReferralCode
    {
        private ReferralCode()
        {
            Code = string.Empty;
        }

        public Guid UserId { get; private set; }

        public string Code { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public static ReferralCode Tao(Guid userId, string code, DateTimeOffset luc)
        {
            ReferralCode r = new ReferralCode();
            r.UserId = userId;
            r.Code = code;
            r.CreatedAt = luc;
            return r;
        }
    }

    /// <summary>Quan he gioi thieu. Khoa chinh = nguoi duoc moi: moi nguoi chi co MOT nguoi gioi thieu.</summary>
    public sealed class Referral
    {
        private Referral()
        {
        }

        public Guid ReferredId { get; private set; }

        public Guid ReferrerId { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        /// <summary>sha256(ip + muoi) luc nhap ma — dem tai khoan cung IP cua mot nguoi gioi thieu.</summary>
        public string? ClaimIpHash { get; private set; }

        /// <summary>active (da tinh hoa hong) | flagged (nghi lam dung, cho admin) | rejected (admin bac).</summary>
        public string Status { get; private set; } = TrangThaiGioiThieu.Active;

        /// <summary>Cac dau hieu nghi ngo (ngan cach dau phay) khi flagged.</summary>
        public string? FlagReasons { get; private set; }

        public DateTimeOffset? DecidedAt { get; private set; }

        public Guid? DecidedBy { get; private set; }

        public static Referral Tao(Guid referrerId, Guid referredId, DateTimeOffset luc)
        {
            return Tao(referrerId, referredId, luc, null, null);
        }

        public static Referral Tao(Guid referrerId, Guid referredId, DateTimeOffset luc, string? ipHash, string? dauHieu)
        {
            Referral r = new Referral();
            r.ReferrerId = referrerId;
            r.ReferredId = referredId;
            r.CreatedAt = luc;
            r.ClaimIpHash = ipHash;
            r.FlagReasons = dauHieu;
            r.Status = dauHieu == null ? TrangThaiGioiThieu.Active : TrangThaiGioiThieu.Flagged;
            return r;
        }

        /// <summary>Admin xet don nghi ngo. Tra false neu don khong con o trang thai flagged.</summary>
        public bool Xet(bool chapNhan, Guid adminId, DateTimeOffset luc)
        {
            if (Status != TrangThaiGioiThieu.Flagged)
            {
                return false;
            }

            Status = chapNhan ? TrangThaiGioiThieu.Active : TrangThaiGioiThieu.Rejected;
            DecidedAt = luc;
            DecidedBy = adminId;
            return true;
        }
    }

    public static class TrangThaiGioiThieu
    {
        public const string Active = "active";
        public const string Flagged = "flagged";
        public const string Rejected = "rejected";
    }

    /// <summary>Ban sao toi thieu tu user.registered: de biet tai khoan con trong cua so nhap ma gioi thieu.</summary>
    public sealed class KnownUser
    {
        private KnownUser()
        {
        }

        public Guid UserId { get; private set; }

        public DateTimeOffset RegisteredAt { get; private set; }

        public static KnownUser Tao(Guid userId, DateTimeOffset registeredAt)
        {
            KnownUser u = new KnownUser();
            u.UserId = userId;
            u.RegisteredAt = registeredAt;
            return u;
        }
    }
}
