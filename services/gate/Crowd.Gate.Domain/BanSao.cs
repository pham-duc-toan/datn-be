using System;
using Crowd.Labeling;

namespace Crowd.Gate.Domain
{
    public enum TrangThaiDuAnCong
    {
        Running,
        Paused,

        /// <summary>Huy / hoan thanh: khong bao gio phuc vu lai.</summary>
        Closed,
    }

    /// <summary>
    /// Ban sao du an tu project.published (chi du an BAT kenh cong link). Tap nhan co hop
    /// voi khach vang lai khong thi GateCatalog kiem luc nap (LuatCongLink).
    /// </summary>
    public sealed class DuAnCong
    {
        private DuAnCong()
        {
            Modality = string.Empty;
            LabelSchemaJson = string.Empty;
        }

        public Guid ProjectId { get; private set; }

        public Guid OwnerId { get; private set; }

        public string Modality { get; private set; }

        /// <summary>Tap nhan dang chuan (jsonb) — doc qua LabelSchema.Doc khi can.</summary>
        public string LabelSchemaJson { get; private set; }

        public long UnitPriceVnd { get; private set; }

        public long PlatformFeeVnd { get; private set; }

        public TrangThaiDuAnCong Status { get; private set; }

        public DateTimeOffset UpdatedAt { get; private set; }

        public static DuAnCong Tao(Guid projectId, Guid ownerId, string modality, string labelSchemaJson, long unitPriceVnd, long platformFeeVnd, DateTimeOffset luc)
        {
            DuAnCong d = new DuAnCong();
            d.ProjectId = projectId;
            d.OwnerId = ownerId;
            d.Modality = modality;
            d.LabelSchemaJson = labelSchemaJson;
            d.UnitPriceVnd = unitPriceVnd;
            d.PlatformFeeVnd = platformFeeVnd;
            d.Status = TrangThaiDuAnCong.Running;
            d.UpdatedAt = luc;
            return d;
        }

        public void DatTrangThai(TrangThaiDuAnCong moi, DateTimeOffset luc)
        {
            // Da dong thi giu dong (event resume den tre sau cancelled khong mo lai).
            if (Status == TrangThaiDuAnCong.Closed)
            {
                return;
            }

            Status = moi;
            UpdatedAt = luc;
        }

    }

    /// <summary>
    /// Ngan sach cong link con lai cua du an (gate.budget_changed — ledger la nguon su that).
    /// TACH khoi DuAnCong: event ngan sach co the toi TRUOC project.published (hai queue
    /// khac nhau) — giu rieng thi khong mat, khong phai thu lai.
    /// </summary>
    public sealed class NganSachCong
    {
        private NganSachCong()
        {
        }

        public Guid ProjectId { get; private set; }

        public long RemainingVnd { get; private set; }

        /// <summary>So thu tu da ap dung — bo qua event cu den tre.</summary>
        public long Sequence { get; private set; }

        public DateTimeOffset UpdatedAt { get; private set; }

        public static NganSachCong Tao(Guid projectId)
        {
            NganSachCong n = new NganSachCong();
            n.ProjectId = projectId;
            return n;
        }

        /// <summary>Tra ve false neu event cu hon ban dang co.</summary>
        public bool Dat(long conLaiVnd, long sequence, DateTimeOffset luc)
        {
            if (sequence <= Sequence)
            {
                return false;
            }

            RemainingVnd = conLaiVnd;
            Sequence = sequence;
            UpdatedAt = luc;
            return true;
        }
    }

    /// <summary>Ban sao mau tu dataset.ingested — moi du an (chua biet du an nao se bat cong link luc nap).</summary>
    public sealed class MauCong
    {
        private MauCong()
        {
            Modality = string.Empty;
        }

        public Guid SampleId { get; private set; }

        public Guid ProjectId { get; private set; }

        public string Modality { get; private set; }

        public string? StorageKey { get; private set; }

        public RawJson? Content { get; private set; }

        public RawJson Metadata { get; private set; } = RawJson.Tu("{}");

        public static MauCong Tao(Guid sampleId, Guid projectId, string modality, string? storageKey, RawJson? content, RawJson metadata)
        {
            MauCong m = new MauCong();
            m.SampleId = sampleId;
            m.ProjectId = projectId;
            m.Modality = modality;
            m.StorageKey = storageKey;
            m.Content = content;
            m.Metadata = metadata;
            return m;
        }
    }

    /// <summary>
    /// Cau vang KIEM TRA (GoldPurpose.QualityCheck) tu gold_set.updated. Cau cua bai test
    /// dau vao KHONG dua len trang cong khai — khach vang lai nhin mai se thuoc de.
    /// Dap an chi nam o day, KHONG BAO GIO ra client (VD-Q-01).
    /// </summary>
    public sealed class CauVangCong
    {
        private CauVangCong()
        {
            TaskType = string.Empty;
            DataJson = string.Empty;
        }

        public Guid SampleId { get; private set; }

        public Guid ProjectId { get; private set; }

        public string TaskType { get; private set; }

        public int SchemaVersion { get; private set; }

        public string DataJson { get; private set; }

        public static CauVangCong Tao(Guid sampleId, Guid projectId, LabelPayload dapAn)
        {
            if (dapAn == null)
            {
                throw new ArgumentNullException(nameof(dapAn));
            }

            CauVangCong c = new CauVangCong();
            c.SampleId = sampleId;
            c.ProjectId = projectId;
            c.TaskType = dapAn.TaskType;
            c.SchemaVersion = dapAn.SchemaVersion;
            c.DataJson = dapAn.DataJson;
            return c;
        }

        public LabelPayload DapAn()
        {
            return LabelPayload.TuLuuTru(TaskType, SchemaVersion, DataJson);
        }
    }

    /// <summary>Ban sao link dang hoat dong tu link.activated / link.disabled.</summary>
    public sealed class LinkCong
    {
        private LinkCong()
        {
            Code = string.Empty;
            DestinationUrl = string.Empty;
        }

        public Guid LinkId { get; private set; }

        public string Code { get; private set; }

        public Guid OwnerId { get; private set; }

        public string DestinationUrl { get; private set; }

        public string? PasswordHash { get; private set; }

        public DateTimeOffset? ExpiresAt { get; private set; }

        public Guid? CampaignId { get; private set; }

        public string? CreatorIpHash { get; private set; }

        public bool Active { get; private set; }

        public DateTimeOffset UpdatedAt { get; private set; }

        public static LinkCong Tao(Guid linkId, string code, Guid ownerId, DateTimeOffset luc)
        {
            LinkCong l = new LinkCong();
            l.LinkId = linkId;
            l.Code = code;
            l.OwnerId = ownerId;
            l.UpdatedAt = luc;
            return l;
        }

        public void KichHoat(string destinationUrl, string? passwordHash, DateTimeOffset? expiresAt, Guid? campaignId, string? creatorIpHash, DateTimeOffset luc)
        {
            DestinationUrl = destinationUrl;
            PasswordHash = passwordHash;
            ExpiresAt = expiresAt;
            CampaignId = campaignId;
            CreatorIpHash = creatorIpHash;
            Active = true;
            UpdatedAt = luc;
        }

        public void VoHieuHoa(DateTimeOffset luc)
        {
            Active = false;
            UpdatedAt = luc;
        }

        public bool PhucVuDuoc(DateTimeOffset luc)
        {
            return Active && (!ExpiresAt.HasValue || luc < ExpiresAt.Value);
        }
    }
}
