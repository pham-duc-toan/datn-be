using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.Contracts.Ledger;
using Crowd.Contracts.Link;
using Crowd.Contracts.Project;
using Crowd.Gate.Api.Services;
using Crowd.Gate.Domain;
using Crowd.Gate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Gate.Api.Consumers
{
    // GIAO KEO: KHONG SaveChanges — IdempotencyGuard luu + ghi processed_events + commit
    // MOT lan. Moi processor bao GateCatalog nap lai de duong nong thay thay doi som.

    /// <summary>project.published: chep du an BAT kenh cong link (tap nhan, don gia, phi da chot).</summary>
    public sealed class ProjectPublishedProcessor : IEventProcessor<ProjectPublished>
    {
        private readonly GateDbContext _db;
        private readonly GateCatalog _catalog;

        public ProjectPublishedProcessor(GateDbContext db, GateCatalog catalog)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            _db = db;
            _catalog = catalog;
        }

        public async Task XuLyAsync(EventEnvelope<ProjectPublished> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            ProjectPublished p = envelope.Payload;
            if (!p.AllowLinkGateway)
            {
                return;
            }

            if (await _db.Projects.AnyAsync(x => x.ProjectId == p.ProjectId, ct))
            {
                return;
            }

            _db.Projects.Add(DuAnCong.Tao(
                p.ProjectId, p.OwnerId, p.Modality, p.LabelSchema.Json, p.UnitPriceVnd, p.PlatformFeeVnd, envelope.OccurredAt));
            _catalog.YeuCauNapLai();
        }
    }

    /// <summary>Vong doi du an sau publish: tam dung / tiep tuc / huy / hoan thanh.</summary>
    public sealed class ProjectStatusProcessors :
        IEventProcessor<ProjectPaused>,
        IEventProcessor<ProjectResumed>,
        IEventProcessor<ProjectCancelled>,
        IEventProcessor<ProjectCompleted>
    {
        private readonly GateDbContext _db;
        private readonly GateCatalog _catalog;

        public ProjectStatusProcessors(GateDbContext db, GateCatalog catalog)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            _db = db;
            _catalog = catalog;
        }

        public Task XuLyAsync(EventEnvelope<ProjectPaused> envelope, CancellationToken ct)
        {
            return DatAsync(envelope.Payload.ProjectId, TrangThaiDuAnCong.Paused, envelope.OccurredAt, ct);
        }

        public Task XuLyAsync(EventEnvelope<ProjectResumed> envelope, CancellationToken ct)
        {
            return DatAsync(envelope.Payload.ProjectId, TrangThaiDuAnCong.Running, envelope.OccurredAt, ct);
        }

        public Task XuLyAsync(EventEnvelope<ProjectCancelled> envelope, CancellationToken ct)
        {
            return DatAsync(envelope.Payload.ProjectId, TrangThaiDuAnCong.Closed, envelope.OccurredAt, ct);
        }

        public Task XuLyAsync(EventEnvelope<ProjectCompleted> envelope, CancellationToken ct)
        {
            return DatAsync(envelope.Payload.ProjectId, TrangThaiDuAnCong.Closed, envelope.OccurredAt, ct);
        }

        private async Task DatAsync(Guid projectId, TrangThaiDuAnCong moi, DateTimeOffset luc, CancellationToken ct)
        {
            // Du an khong bat cong link thi gate khong co ban sao — khong lam gi.
            DuAnCong? d = await _db.Projects.FirstOrDefaultAsync(x => x.ProjectId == projectId, ct);
            if (d == null)
            {
                return;
            }

            d.DatTrangThai(moi, luc);
            _catalog.YeuCauNapLai();
        }
    }

    /// <summary>dataset.ingested: chep mau (moi du an — luc nap chua biet du an co bat cong link khong).</summary>
    public sealed class DatasetIngestedProcessor : IEventProcessor<DatasetIngested>
    {
        private readonly GateDbContext _db;
        private readonly GateCatalog _catalog;

        public DatasetIngestedProcessor(GateDbContext db, GateCatalog catalog)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            _db = db;
            _catalog = catalog;
        }

        public async Task XuLyAsync(EventEnvelope<DatasetIngested> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            DatasetIngested p = envelope.Payload;
            List<Guid> ids = p.Samples.Select(s => s.SampleId).ToList();
            HashSet<Guid> daCo = (await _db.Samples.Where(s => ids.Contains(s.SampleId)).Select(s => s.SampleId).ToListAsync(ct)).ToHashSet();

            foreach (IngestedSample s in p.Samples)
            {
                if (!daCo.Contains(s.SampleId))
                {
                    _db.Samples.Add(MauCong.Tao(s.SampleId, p.ProjectId, s.Modality, s.StorageKey, s.Content, s.Metadata));
                }
            }

            _catalog.YeuCauNapLai();
        }
    }

    /// <summary>gold_set.updated: thay TOAN BO cau vang kiem tra cua du an (chi QualityCheck).</summary>
    public sealed class GoldSetUpdatedProcessor : IEventProcessor<GoldSetUpdated>
    {
        private readonly GateDbContext _db;
        private readonly GateCatalog _catalog;

        public GoldSetUpdatedProcessor(GateDbContext db, GateCatalog catalog)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            _db = db;
            _catalog = catalog;
        }

        public async Task XuLyAsync(EventEnvelope<GoldSetUpdated> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            GoldSetUpdated p = envelope.Payload;
            List<CauVangCong> cu = await _db.GoldItems.Where(g => g.ProjectId == p.ProjectId).ToListAsync(ct);
            _db.GoldItems.RemoveRange(cu);

            foreach (GoldSetItem g in p.Items)
            {
                if (g.Purpose == GoldPurpose.QualityCheck)
                {
                    _db.GoldItems.Add(CauVangCong.Tao(g.SampleId, p.ProjectId, g.ExpectedPayload));
                }
            }

            _catalog.YeuCauNapLai();
        }
    }

    /// <summary>link.activated / link.disabled: ban sao link dang chay.</summary>
    public sealed class LinkProcessors : IEventProcessor<LinkActivated>, IEventProcessor<LinkDisabled>
    {
        private readonly GateDbContext _db;
        private readonly GateCatalog _catalog;

        public LinkProcessors(GateDbContext db, GateCatalog catalog)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            _db = db;
            _catalog = catalog;
        }

        public async Task XuLyAsync(EventEnvelope<LinkActivated> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            LinkActivated p = envelope.Payload;
            LinkCong? l = await _db.Links.FirstOrDefaultAsync(x => x.LinkId == p.LinkId, ct);
            if (l == null)
            {
                l = LinkCong.Tao(p.LinkId, p.Code, p.OwnerId, envelope.OccurredAt);
                _db.Links.Add(l);
            }
            else if (!l.Active && l.UpdatedAt > envelope.OccurredAt)
            {
                // link.disabled MOI hon da toi truoc: khong bat lai.
                return;
            }

            l.KichHoat(p.DestinationUrl, p.PasswordHash, p.ExpiresAt, p.CampaignId, p.CreatorIpHash, envelope.OccurredAt);
            _catalog.YeuCauNapLai();
        }

        public async Task XuLyAsync(EventEnvelope<LinkDisabled> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            LinkCong? l = await _db.Links.FirstOrDefaultAsync(x => x.LinkId == envelope.Payload.LinkId, ct);
            if (l == null)
            {
                return;
            }

            l.VoHieuHoa(envelope.OccurredAt);
            _catalog.YeuCauNapLai();
        }
    }

    /// <summary>gate.budget_changed (ledger): ngan sach cong link con lai cua du an.</summary>
    public sealed class GateBudgetChangedProcessor : IEventProcessor<GateBudgetChanged>
    {
        private readonly GateDbContext _db;
        private readonly GateCatalog _catalog;

        public GateBudgetChangedProcessor(GateDbContext db, GateCatalog catalog)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            _db = db;
            _catalog = catalog;
        }

        public async Task XuLyAsync(EventEnvelope<GateBudgetChanged> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            GateBudgetChanged p = envelope.Payload;
            NganSachCong? n = await _db.Budgets.FirstOrDefaultAsync(x => x.ProjectId == p.ProjectId, ct);
            if (n == null)
            {
                n = NganSachCong.Tao(p.ProjectId);
                _db.Budgets.Add(n);
            }

            if (n.Dat(p.RemainingVnd, p.Sequence, envelope.OccurredAt))
            {
                _catalog.YeuCauNapLai();
            }
        }
    }
}
