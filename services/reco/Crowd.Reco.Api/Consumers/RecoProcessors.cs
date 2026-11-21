using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.BuildingBlocks.Settings;
using Crowd.Contracts.Identity;
using Crowd.Contracts.Project;
using Crowd.Contracts.Quality;
using Crowd.Contracts.Tasking;
using Crowd.Reco.Api.Services;
using Crowd.Reco.Domain;
using Crowd.Reco.Infrastructure.Persistence;
using Crowd.Reco.Infrastructure.Redis;

namespace Crowd.Reco.Api.Consumers
{
    // Consumer cua reco-svc (kien truc 3.39). Processor KHONG SaveChanges — EventConsumer boc trong processed_events +
    // transaction. Bo dem so thich cong bang SQL trong CUNG transaction; Redis chi la cache (xoa / dat idempotent).

    /// <summary>user.registered v2 → so khoi dau bo dem tu the chon luc dang ky (a 2, b 0 — muc hop 0,75).</summary>
    public sealed class UserRegisteredProcessor : IEventProcessor<UserRegistered>
    {
        private readonly RecoDbContext _db;
        private readonly RecoRedis _redis;
        private readonly ISettings _settings;

        public UserRegisteredProcessor(RecoDbContext db, RecoRedis redis, ISettings settings)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (redis == null)
            {
                throw new ArgumentNullException(nameof(redis));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            _db = db;
            _redis = redis;
            _settings = settings;
        }

        public async Task XuLyAsync(EventEnvelope<UserRegistered> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            UserRegistered p = envelope.Payload;
            List<CongBoDem> ds = HanhVi.TuDangKy(p.Domains, p.Languages, p.Modalities)
                .Select(x => new CongBoDem { UserId = p.UserId, Loai = x.Loai, Ma = x.Ma, WA = x.WA, WB = x.WB })
                .ToList();
            if (ds.Count == 0)
            {
                return;
            }

            await BoDemStore.CongAsync(_db, ds, envelope.OccurredAt, _settings.SoNguyen(SettingKeys.RecoAffinityHalfLifeDays), ct);
            await _redis.XoaSoThichAsync(new Guid[] { p.UserId });
        }
    }

    /// <summary>attempt.started → hanh vi "bat dau" (a + 0,2) + nhan "co bat dau" cho mo hinh.</summary>
    public sealed class AttemptStartedProcessor : IEventProcessor<AttemptStarted>
    {
        private readonly HanhViService _hanhVi;

        public AttemptStartedProcessor(HanhViService hanhVi)
        {
            if (hanhVi == null)
            {
                throw new ArgumentNullException(nameof(hanhVi));
            }

            _hanhVi = hanhVi;
        }

        public Task XuLyAsync(EventEnvelope<AttemptStarted> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            AttemptStarted p = envelope.Payload;
            return _hanhVi.GhiTuEventAsync(envelope.EventId, p.LabelerId, p.ProjectId, HanhVi.BatDau, p.StartedAt, ct);
        }
    }

    /// <summary>attempt.submitted → hanh vi "nop" (a + 0,5) + loai du an khoi goi y cua nguoi nay.</summary>
    public sealed class AttemptSubmittedProcessor : IEventProcessor<AttemptSubmitted>
    {
        private readonly HanhViService _hanhVi;
        private readonly RecoRedis _redis;

        public AttemptSubmittedProcessor(HanhViService hanhVi, RecoRedis redis)
        {
            if (hanhVi == null)
            {
                throw new ArgumentNullException(nameof(hanhVi));
            }

            if (redis == null)
            {
                throw new ArgumentNullException(nameof(redis));
            }

            _hanhVi = hanhVi;
            _redis = redis;
        }

        public async Task XuLyAsync(EventEnvelope<AttemptSubmitted> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            AttemptSubmitted p = envelope.Payload;
            await _redis.DatLoaiTruAsync(p.LabelerId, RecoRedis.TruongDaNop(p.ProjectId));
            await _hanhVi.GhiTuEventAsync(envelope.EventId, p.LabelerId, p.ProjectId, HanhVi.Nop, p.SubmittedAt, ct);
        }
    }

    /// <summary>attempt.abandoned (tu bo / het gio) → hanh vi "bo do" (b + 0,5).</summary>
    public sealed class AttemptAbandonedProcessor : IEventProcessor<AttemptAbandoned>
    {
        private readonly HanhViService _hanhVi;

        public AttemptAbandonedProcessor(HanhViService hanhVi)
        {
            if (hanhVi == null)
            {
                throw new ArgumentNullException(nameof(hanhVi));
            }

            _hanhVi = hanhVi;
        }

        public Task XuLyAsync(EventEnvelope<AttemptAbandoned> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            AttemptAbandoned p = envelope.Payload;
            return _hanhVi.GhiTuEventAsync(envelope.EventId, p.LabelerId, p.ProjectId, HanhVi.BoDo, p.EndedAt, ct);
        }
    }

    /// <summary>project.graded → moi bai: dat (a + 1, bo dem dat theo cong cu a + 1) hoac truot (b + 1). Nhan P(dat) cho mo hinh.</summary>
    public sealed class ProjectGradedProcessor : IEventProcessor<ProjectGraded>
    {
        private readonly HanhViService _hanhVi;

        public ProjectGradedProcessor(HanhViService hanhVi)
        {
            if (hanhVi == null)
            {
                throw new ArgumentNullException(nameof(hanhVi));
            }

            _hanhVi = hanhVi;
        }

        public async Task XuLyAsync(EventEnvelope<ProjectGraded> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            ProjectGraded p = envelope.Payload;
            foreach (GradedAttempt a in p.Attempts)
            {
                // Ma su kien rieng tung bai (tat dinh theo event + bai): ClickHouse gop ban giao lai.
                Guid maRieng = TaoMa(envelope.EventId, a.AttemptId);
                await _hanhVi.GhiTuEventAsync(maRieng, a.LabelerId, p.ProjectId, a.Passed ? HanhVi.Dat : HanhVi.Truot, p.GradedAt, ct);
            }
        }

        private static Guid TaoMa(Guid a, Guid b)
        {
            byte[] x = a.ToByteArray();
            byte[] y = b.ToByteArray();
            for (int i = 0; i < 16; i++)
            {
                x[i] = (byte)(x[i] ^ y[i]);
            }

            return new Guid(x);
        }
    }

    /// <summary>member.added → du an rieng tu nguoi nay duoc thay.</summary>
    public sealed class MemberAddedProcessor : IEventProcessor<MemberAdded>
    {
        private readonly RecoRedis _redis;

        public MemberAddedProcessor(RecoRedis redis)
        {
            if (redis == null)
            {
                throw new ArgumentNullException(nameof(redis));
            }

            _redis = redis;
        }

        public Task XuLyAsync(EventEnvelope<MemberAdded> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            return _redis.DatLoaiTruAsync(envelope.Payload.UserId, RecoRedis.TruongThanhVien(envelope.Payload.ProjectId));
        }
    }

    public sealed class MemberRemovedProcessor : IEventProcessor<MemberRemoved>
    {
        private readonly RecoRedis _redis;

        public MemberRemovedProcessor(RecoRedis redis)
        {
            if (redis == null)
            {
                throw new ArgumentNullException(nameof(redis));
            }

            _redis = redis;
        }

        public Task XuLyAsync(EventEnvelope<MemberRemoved> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            return _redis.XoaLoaiTruAsync(envelope.Payload.UserId, RecoRedis.TruongThanhVien(envelope.Payload.ProjectId));
        }
    }

    public sealed class MemberBlockedProcessor : IEventProcessor<MemberBlocked>
    {
        private readonly RecoRedis _redis;

        public MemberBlockedProcessor(RecoRedis redis)
        {
            if (redis == null)
            {
                throw new ArgumentNullException(nameof(redis));
            }

            _redis = redis;
        }

        public Task XuLyAsync(EventEnvelope<MemberBlocked> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            return _redis.DatLoaiTruAsync(envelope.Payload.UserId, RecoRedis.TruongBiChan(envelope.Payload.ProjectId));
        }
    }

    public sealed class MemberUnblockedProcessor : IEventProcessor<MemberUnblocked>
    {
        private readonly RecoRedis _redis;

        public MemberUnblockedProcessor(RecoRedis redis)
        {
            if (redis == null)
            {
                throw new ArgumentNullException(nameof(redis));
            }

            _redis = redis;
        }

        public Task XuLyAsync(EventEnvelope<MemberUnblocked> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            return _redis.XoaLoaiTruAsync(envelope.Payload.UserId, RecoRedis.TruongBiChan(envelope.Payload.ProjectId));
        }
    }

    /// <summary>owner_blocklist.changed → an / hien moi du an cua doanh nghiep voi labeler nay.</summary>
    public sealed class OwnerBlocklistChangedProcessor : IEventProcessor<OwnerBlocklistChanged>
    {
        private readonly RecoRedis _redis;

        public OwnerBlocklistChangedProcessor(RecoRedis redis)
        {
            if (redis == null)
            {
                throw new ArgumentNullException(nameof(redis));
            }

            _redis = redis;
        }

        public Task XuLyAsync(EventEnvelope<OwnerBlocklistChanged> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            OwnerBlocklistChanged p = envelope.Payload;
            string truong = RecoRedis.TruongDoanhNghiepChan(p.OwnerId);
            return p.Blocked ? _redis.DatLoaiTruAsync(p.LabelerId, truong) : _redis.XoaLoaiTruAsync(p.LabelerId, truong);
        }
    }

    /// <summary>
    /// Du an mo / tam dung / chay lai / huy / dong thu bai → nap lai bo nho NGAY (khong doi chu ky 30 giay). Mot
    /// processor chung cho nhieu loai event: chi danh thuc worker nap (gop nhieu event thanh mot lan nap).
    /// </summary>
    public sealed class NapLaiKhiDoiDuAn<TPayload> : IEventProcessor<TPayload>
        where TPayload : class, IEventPayload
    {
        private readonly CatalogGoiY _catalog;

        public NapLaiKhiDoiDuAn(CatalogGoiY catalog)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            _catalog = catalog;
        }

        public Task XuLyAsync(EventEnvelope<TPayload> envelope, CancellationToken ct)
        {
            _catalog.YeuCauNapLai();
            return Task.CompletedTask;
        }
    }
}
