using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.BuildingBlocks.Settings;
using Crowd.Contracts.Reco;
using Crowd.Reco.Domain;
using Crowd.Reco.Infrastructure.Persistence;
using Crowd.Reco.Infrastructure.Source;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crowd.Reco.Api.Services
{
    /// <summary>
    /// Nhac du an da THICH sap het han (kien truc 3.40): du an dang nhan bai co han chot trong reco.liked_reminder_hours toi,
    /// labeler da thich ma CHUA nop bai → event liked_project.deadline_soon (outbox) → notification-svc gui thong bao.
    /// Moi (labeler, du an) MOT lan: "danh dau da nhac" bang UPDATE ... WHERE reminded_at IS NULL — nhieu instance cung quet
    /// thi chi mot instance thang, event vao outbox CUNG transaction voi dau danh dau.
    /// </summary>
    public sealed class NhacDaThichService
    {
        public const string Producer = "reco-svc";

        private readonly RecoDbContext _db;
        private readonly IOutboxWriter _outbox;
        private readonly CatalogGoiY _catalog;
        private readonly NguonDuAn _nguon;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;
        private readonly ILogger<NhacDaThichService> _logger;

        public NhacDaThichService(
            RecoDbContext db,
            IOutboxWriter outbox,
            CatalogGoiY catalog,
            NguonDuAn nguon,
            ISettings settings,
            TimeProvider clock,
            ILogger<NhacDaThichService> logger)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (outbox == null)
            {
                throw new ArgumentNullException(nameof(outbox));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (nguon == null)
            {
                throw new ArgumentNullException(nameof(nguon));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _db = db;
            _outbox = outbox;
            _catalog = catalog;
            _nguon = nguon;
            _settings = settings;
            _clock = clock;
            _logger = logger;
        }

        /// <summary>Mot vong quet. Tra so nhac da phat.</summary>
        public async Task<int> QuetAsync(CancellationToken ct)
        {
            DateTimeOffset bayGio = _clock.GetUtcNow();
            DateTimeOffset den = bayGio.AddHours(_settings.SoNguyen(SettingKeys.RecoLikedReminderHours));
            Dictionary<Guid, DuAnGoiY> sapHet = _catalog.Anh.DuAn.Values
                .Where(p => p.Deadline.HasValue && p.Deadline.Value > bayGio && p.Deadline.Value <= den)
                .ToDictionary(p => p.Id);
            if (sapHet.Count == 0)
            {
                return 0;
            }

            List<Guid> ids = sapHet.Keys.ToList();
            List<LikedProject> chuaNhac = await _db.LikedProjects.AsNoTracking()
                .Where(x => x.RemindedAt == null && ids.Contains(x.ProjectId))
                .ToListAsync(ct);
            if (chuaNhac.Count == 0)
            {
                return 0;
            }

            HashSet<KeyValuePair<Guid, Guid>> daNop = await _nguon.DocDaNopAsync(chuaNhac.Select(x => x.ProjectId).Distinct().ToList(), ct);
            int soNhac = 0;
            var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                foreach (LikedProject l in chuaNhac)
                {
                    if (daNop.Contains(new KeyValuePair<Guid, Guid>(l.UserId, l.ProjectId)))
                    {
                        continue;
                    }

                    Guid u = l.UserId;
                    Guid p = l.ProjectId;
                    int giu = await _db.LikedProjects
                        .Where(x => x.UserId == u && x.ProjectId == p && x.RemindedAt == null)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.RemindedAt, bayGio), ct);
                    if (giu != 1)
                    {
                        continue;
                    }

                    DuAnGoiY duAn = sapHet[p];
                    _outbox.Enqueue(EventEnvelope.Create(
                        producer: Producer,
                        correlationId: Guid.NewGuid(),
                        payload: new LikedProjectDeadlineSoon
                        {
                            UserId = u,
                            ProjectId = p,
                            ProjectName = duAn.Name,
                            Deadline = duAn.Deadline!.Value,
                        }));
                    soNhac++;
                }

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            finally
            {
                await tx.DisposeAsync();
            }

            if (soNhac > 0)
            {
                _logger.LogInformation("Nhac {SoNhac} du an da thich sap het han", soNhac);
            }

            return soNhac;
        }
    }
}
