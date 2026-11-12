using System;
using Crowd.Labeling;

namespace Crowd.Gate.Domain
{
    public enum TrangThaiDuAnCong
    {
        Running,
        Paused,
    }

    /// <summary>
    /// Du an dang len cong link — doc tu view v_gate_projects cua project-svc (kien truc 3.32), KHONG luu o gate.
    /// Luat "du an nao len cong" (da publish, bat cong, chua dong) nam trong view; tap nhan co hop voi khach vang lai
    /// khong thi GateCatalog kiem luc nap (LuatCongLink).
    /// </summary>
    public sealed class DuAnCong
    {
        public DuAnCong(Guid projectId, Guid ownerId, string modality, string labelSchemaJson, long unitPriceVnd, long platformFeeVnd, TrangThaiDuAnCong status)
        {
            if (modality == null)
            {
                throw new ArgumentNullException(nameof(modality));
            }

            if (labelSchemaJson == null)
            {
                throw new ArgumentNullException(nameof(labelSchemaJson));
            }

            ProjectId = projectId;
            OwnerId = ownerId;
            Modality = modality;
            LabelSchemaJson = labelSchemaJson;
            UnitPriceVnd = unitPriceVnd;
            PlatformFeeVnd = platformFeeVnd;
            Status = status;
        }

        public Guid ProjectId { get; }

        public Guid OwnerId { get; }

        public string Modality { get; }

        /// <summary>Tap nhan dang chuan (jsonb) — doc qua LabelSchema.Doc khi can.</summary>
        public string LabelSchemaJson { get; }

        public long UnitPriceVnd { get; }

        public long PlatformFeeVnd { get; }

        public TrangThaiDuAnCong Status { get; }
    }

    /// <summary>Mau cua du an len cong — doc tu view v_gate_samples (mot lan moi du an: sau publish mau khong doi).</summary>
    public sealed class MauCong
    {
        public MauCong(Guid sampleId, Guid projectId, string modality, string? storageKey, RawJson? content, RawJson metadata)
        {
            if (modality == null)
            {
                throw new ArgumentNullException(nameof(modality));
            }

            if (metadata == null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }

            SampleId = sampleId;
            ProjectId = projectId;
            Modality = modality;
            StorageKey = storageKey;
            Content = content;
            Metadata = metadata;
        }

        public Guid SampleId { get; }

        public Guid ProjectId { get; }

        public string Modality { get; }

        public string? StorageKey { get; }

        public RawJson? Content { get; }

        public RawJson Metadata { get; }
    }

    /// <summary>
    /// Cau vang KIEM TRA (QualityCheck) — doc tu view v_gate_gold cung luc voi mau. Cau cua bai test dau vao KHONG
    /// len trang cong khai (view da loc). Dap an chi nam trong bo nho gate, KHONG BAO GIO ra client (VD-Q-01).
    /// </summary>
    public sealed class CauVangCong
    {
        public CauVangCong(Guid sampleId, Guid projectId, LabelPayload dapAn)
        {
            if (dapAn == null)
            {
                throw new ArgumentNullException(nameof(dapAn));
            }

            SampleId = sampleId;
            ProjectId = projectId;
            DapAn = dapAn;
        }

        public Guid SampleId { get; }

        public Guid ProjectId { get; }

        public LabelPayload DapAn { get; }
    }

    /// <summary>Link dang chay — doc tu view v_gate_links cua link-svc (chi link Active).</summary>
    public sealed class LinkCong
    {
        public LinkCong(
            Guid linkId,
            string code,
            Guid ownerId,
            string destinationUrl,
            string? passwordHash,
            DateTimeOffset? expiresAt,
            Guid? campaignId,
            string? creatorIpHash)
        {
            if (code == null)
            {
                throw new ArgumentNullException(nameof(code));
            }

            if (destinationUrl == null)
            {
                throw new ArgumentNullException(nameof(destinationUrl));
            }

            LinkId = linkId;
            Code = code;
            OwnerId = ownerId;
            DestinationUrl = destinationUrl;
            PasswordHash = passwordHash;
            ExpiresAt = expiresAt;
            CampaignId = campaignId;
            CreatorIpHash = creatorIpHash;
        }

        public Guid LinkId { get; }

        public string Code { get; }

        public Guid OwnerId { get; }

        public string DestinationUrl { get; }

        public string? PasswordHash { get; }

        public DateTimeOffset? ExpiresAt { get; }

        public Guid? CampaignId { get; }

        public string? CreatorIpHash { get; }

        /// <summary>View chi tra link Active; con lai la kiem het han.</summary>
        public bool PhucVuDuoc(DateTimeOffset luc)
        {
            return !ExpiresAt.HasValue || luc < ExpiresAt.Value;
        }
    }
}
