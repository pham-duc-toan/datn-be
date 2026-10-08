using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.BuildingBlocks.Settings;
using Crowd.Contracts.Admin;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Settings
{
    /// <summary>Ghi gia tri moi vao ban sao DB (version moi hon moi ghi) va bo nho.</summary>
    internal static class BanSao
    {
        public static async Task GhiAsync(
            DbContext db, SettingsStore store, IReadOnlyList<(string Key, string Json, long Version)> moi, DateTimeOffset luc, CancellationToken ct)
        {
            List<string> keys = moi.Select(m => m.Key).ToList();
            Dictionary<string, SettingReplica> co = await db.Set<SettingReplica>()
                .Where(r => keys.Contains(r.Key))
                .ToDictionaryAsync(r => r.Key, StringComparer.Ordinal, ct);

            foreach ((string key, string json, long version) in moi)
            {
                // Khoa khong co trong danh muc cua ban build nay (admin-svc moi hon) — bo qua.
                if (SettingCatalog.Tim(key) == null)
                {
                    continue;
                }

                SettingReplica? r;
                if (co.TryGetValue(key, out r))
                {
                    r.ApDung(json, version, luc);
                }
                else
                {
                    db.Set<SettingReplica>().Add(SettingReplica.Tao(key, json, version, luc));
                }

                store.ApDung(key, json, version);
            }
        }
    }

    /// <summary>setting.changed → ban sao. KHONG SaveChanges: IdempotencyGuard lo.</summary>
    public sealed class SettingChangedProcessor<TDbContext> : IEventProcessor<SettingChanged>
        where TDbContext : DbContext
    {
        private readonly TDbContext _db;
        private readonly SettingsStore _store;

        public SettingChangedProcessor(TDbContext db, SettingsStore store)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            _db = db;
            _store = store;
        }

        public Task XuLyAsync(EventEnvelope<SettingChanged> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            SettingChanged p = envelope.Payload;
            List<(string, string, long)> moi = new List<(string, string, long)> { (p.Key, p.Value.Json, p.SettingVersion) };
            return BanSao.GhiAsync(_db, _store, moi, envelope.OccurredAt, ct);
        }
    }

    /// <summary>settings.snapshot → ban sao (tung khoa, version moi hon moi ghi).</summary>
    public sealed class SettingsSnapshotProcessor<TDbContext> : IEventProcessor<SettingsSnapshot>
        where TDbContext : DbContext
    {
        private readonly TDbContext _db;
        private readonly SettingsStore _store;

        public SettingsSnapshotProcessor(TDbContext db, SettingsStore store)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            _db = db;
            _store = store;
        }

        public Task XuLyAsync(EventEnvelope<SettingsSnapshot> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            List<(string, string, long)> moi = envelope.Payload.Items
                .Select(i => (i.Key, i.Value.Json, i.SettingVersion))
                .ToList();
            return BanSao.GhiAsync(_db, _store, moi, envelope.OccurredAt, ct);
        }
    }
}
