using System;
using Crowd.Link.Api.Exceptions;

namespace Crowd.Link.Api.Entities
{
    public enum LinkStatus
    {
        /// <summary>Vua tao, cho worker quet Safe Browsing — chua phuc vu.</summary>
        PendingScan,

        /// <summary>Dang phuc vu tai /g/{code}.</summary>
        Active,

        /// <summary>Quet phat hien nguy hiem / ten mien bi chan — khong bao gio phuc vu.</summary>
        Blocked,

        /// <summary>Chu link xoa, hoac admin vo hieu hoa vi vi pham.</summary>
        Disabled,
    }

    /// <summary>
    /// Mot link rut gon cua sharer (FS-01). Link dich KHONG doi duoc sau khi tao: doi
    /// dich la phai quet lai, va link dang kiem tien ma doi dich sang trang doc hai
    /// la mot kieu lach kiem duyet — muon doi thi tao link moi.
    /// </summary>
    public sealed class ShortLink
    {
        /// <summary>Do rong cot trong DB.</summary>
        public const int CotMa = 32;
        public const int CotUrl = 2048;
        public const int CotTenMien = 253;
        public const int CotLyDo = 500;

        private ShortLink()
        {
            Code = string.Empty;
            DestinationUrl = string.Empty;
            Domain = string.Empty;
        }

        public Guid Id { get; private set; }

        public Guid OwnerId { get; private set; }

        /// <summary>Ma ngau nhien hoac alias (FS-06). UNIQUE, phan biet hoa thuong.</summary>
        public string Code { get; private set; }

        public string DestinationUrl { get; private set; }

        /// <summary>Ten mien cua link dich (chu thuong) — de chan theo ten mien.</summary>
        public string Domain { get; private set; }

        public string? PasswordHash { get; private set; }

        public DateTimeOffset? ExpiresAt { get; private set; }

        public Guid? CampaignId { get; private set; }

        public LinkStatus Status { get; private set; }

        public string? StatusReason { get; private set; }

        public string? CreatorIpHash { get; private set; }

        /// <summary>So IP khac nhau da bao cao link nay.</summary>
        public int ReportCount { get; private set; }

        /// <summary>Bao cao vuot nguong → cho admin xem (hang doi kiem duyet).</summary>
        public bool NeedsReview { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public DateTimeOffset UpdatedAt { get; private set; }

        public static ShortLink Tao(
            Guid ownerId,
            string code,
            string destinationUrl,
            string domain,
            string? passwordHash,
            DateTimeOffset? expiresAt,
            Guid? campaignId,
            string? creatorIpHash,
            DateTimeOffset luc)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length > CotMa)
            {
                throw new LinkException(400, "ma_khong_hop_le", "Ma link khong hop le.");
            }

            if (expiresAt.HasValue && expiresAt.Value <= luc)
            {
                throw new LinkException(400, "het_han_trong_qua_khu", "Thoi diem het han phai o tuong lai.");
            }

            ShortLink l = new ShortLink();
            l.Id = Guid.CreateVersion7();
            l.OwnerId = ownerId;
            l.Code = code;
            l.DestinationUrl = destinationUrl;
            l.Domain = domain;
            l.PasswordHash = passwordHash;
            l.ExpiresAt = expiresAt;
            l.CampaignId = campaignId;
            l.CreatorIpHash = creatorIpHash;
            l.Status = LinkStatus.PendingScan;
            l.CreatedAt = luc;
            l.UpdatedAt = luc;
            return l;
        }

        public bool DaHetHan(DateTimeOffset luc)
        {
            return ExpiresAt.HasValue && luc >= ExpiresAt.Value;
        }

        /// <summary>Worker quet: link sach → bat dau phuc vu.</summary>
        public void KichHoat(DateTimeOffset luc)
        {
            if (Status != LinkStatus.PendingScan)
            {
                throw new LinkException(409, "link_khong_cho_quet", "Link khong o trang thai cho quet (" + Status + ").");
            }

            Status = LinkStatus.Active;
            StatusReason = null;
            UpdatedAt = luc;
        }

        /// <summary>Worker quet: phat hien nguy hiem → chan vinh vien.</summary>
        public void Chan(string lyDo, DateTimeOffset luc)
        {
            if (Status != LinkStatus.PendingScan)
            {
                throw new LinkException(409, "link_khong_cho_quet", "Link khong o trang thai cho quet (" + Status + ").");
            }

            Status = LinkStatus.Blocked;
            StatusReason = CatNgan(lyDo);
            UpdatedAt = luc;
        }

        /// <summary>Chu link xoa hoac admin vo hieu hoa. Tra ve false neu link da khong phuc vu tu truoc.</summary>
        public bool VoHieuHoa(string lyDo, DateTimeOffset luc)
        {
            if (Status == LinkStatus.Disabled || Status == LinkStatus.Blocked)
            {
                return false;
            }

            Status = LinkStatus.Disabled;
            StatusReason = CatNgan(lyDo);
            NeedsReview = false;
            UpdatedAt = luc;
            return true;
        }

        public void CapNhatTuyChon(string? passwordHash, bool doiMatKhau, DateTimeOffset? expiresAt, Guid? campaignId, DateTimeOffset luc)
        {
            if (Status == LinkStatus.Disabled || Status == LinkStatus.Blocked)
            {
                throw new LinkException(409, "link_da_ngung", "Link da ngung phuc vu, khong sua duoc.");
            }

            if (expiresAt.HasValue && expiresAt.Value <= luc)
            {
                throw new LinkException(400, "het_han_trong_qua_khu", "Thoi diem het han phai o tuong lai.");
            }

            if (doiMatKhau)
            {
                PasswordHash = passwordHash;
            }

            ExpiresAt = expiresAt;
            CampaignId = campaignId;
            UpdatedAt = luc;
        }

        /// <summary>Them mot bao cao (moi IP mot lan — bang link_reports chan trung). Tra ve true neu vua vuot nguong.</summary>
        public bool GhiBaoCao(int nguongKiemDuyet, DateTimeOffset luc)
        {
            ReportCount = ReportCount + 1;
            UpdatedAt = luc;

            if (!NeedsReview && Status == LinkStatus.Active && ReportCount >= nguongKiemDuyet)
            {
                NeedsReview = true;
                return true;
            }

            return false;
        }

        /// <summary>Admin xem xong, link khong vi pham.</summary>
        public void BoQuaBaoCao(DateTimeOffset luc)
        {
            NeedsReview = false;
            UpdatedAt = luc;
        }

        private static string CatNgan(string lyDo)
        {
            string s = lyDo == null ? string.Empty : lyDo.Trim();
            return s.Length > CotLyDo ? s.Substring(0, CotLyDo) : s;
        }
    }
}
