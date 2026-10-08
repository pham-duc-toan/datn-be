using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Admin.Api.Dtos;
using Crowd.Admin.Api.Entities;
using Crowd.Admin.Api.Persistence;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.BuildingBlocks.Settings;
using Crowd.Contracts.Admin;
using Crowd.Labeling;
using Crowd.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crowd.Admin.Api.Services
{
    /// <summary>Loi nghiep vu cua admin-svc → ProblemDetails co "code".</summary>
    public sealed class AdminException : Exception
    {
        public AdminException(int status, string code, string message)
            : base(message)
        {
            Status = status;
            Code = code;
        }

        public int Status { get; }

        public string Code { get; }
    }

    /// <summary>
    /// Quan ly setting he thong: doc, doi (kiem kieu + gioi han + rang buoc giua cac
    /// setting), ghi lich su, phat setting.changed / settings.snapshot qua outbox.
    /// </summary>
    public sealed class SettingService
    {
        public const string Producer = "admin-svc";

        /// <summary>Cap (nho, lon) phai giu nho ≤ lon sau moi lan doi.</summary>
        private static readonly (string Nho, string Lon)[] RangBuocCap = new (string, string)[]
        {
            (SettingKeys.PaymentDepositMinVnd, SettingKeys.PaymentDepositMaxVnd),
            (SettingKeys.ProjectGoldCheckPercentDefault, SettingKeys.ProjectGoldCheckPercentMax),
            (SettingKeys.ProjectEntranceQuestionDefault, SettingKeys.ProjectEntranceQuestionMax),
            (SettingKeys.IdentityPasswordMinLength, SettingKeys.IdentityPasswordMaxLength),
            (SettingKeys.IdentityAccessTokenLifetime, SettingKeys.IdentityRefreshTokenLifetime),
            (SettingKeys.DatasetZipImageMaxBytes, SettingKeys.DatasetZipTotalMaxBytes),
            (SettingKeys.DatasetManifestInlineMaxRows, SettingKeys.DatasetManifestFileMaxRows),
        };

        private readonly AdminDbContext _db;
        private readonly IOutboxWriter _outbox;
        private readonly SettingsStore _store;
        private readonly TimeProvider _clock;
        private readonly ILogger<SettingService> _logger;

        public SettingService(
            AdminDbContext db, IOutboxWriter outbox, SettingsStore store, TimeProvider clock, ILogger<SettingService> logger)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (outbox == null)
            {
                throw new ArgumentNullException(nameof(outbox));
            }

            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
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
            _store = store;
            _clock = clock;
            _logger = logger;
        }

        // =====================================================================
        // DOC
        // =====================================================================

        public async Task<IReadOnlyList<SettingResponse>> DanhSachAsync(string? nhom, CancellationToken ct)
        {
            Dictionary<string, Setting> theoKhoa = await _db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, ct);
            List<SettingResponse> ra = new List<SettingResponse>();

            foreach (SettingDefinition d in SettingCatalog.TatCa)
            {
                if (nhom != null && !string.Equals(d.Group, nhom, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Setting? s;
                theoKhoa.TryGetValue(d.Key, out s);
                ra.Add(TaoResponse(d, s));
            }

            return ra;
        }

        public async Task<SettingResponse> ChiTietAsync(string key, CancellationToken ct)
        {
            SettingDefinition d = DinhNghia(key);
            Setting? s = await _db.Settings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key, ct);
            return TaoResponse(d, s);
        }

        public async Task<IReadOnlyList<SettingHistoryResponse>> LichSuAsync(string key, CancellationToken ct)
        {
            DinhNghia(key);
            List<SettingHistory> ds = await _db.SettingHistory.AsNoTracking()
                .Where(h => h.Key == key)
                .OrderByDescending(h => h.Version)
                .ToListAsync(ct);

            return ds.Select(h => new SettingHistoryResponse
            {
                Version = h.Version,
                OldValue = h.OldValueJson == null ? null : JsonNode.Parse(h.OldValueJson),
                NewValue = JsonNode.Parse(h.NewValueJson),
                ChangedBy = h.ChangedBy,
                ChangedAt = h.ChangedAt,
                Reason = h.Reason,
            }).ToList();
        }

        // =====================================================================
        // DOI
        // =====================================================================

        public async Task<SettingResponse> DoiAsync(string key, UpdateSettingRequest body, Guid admin, CancellationToken ct)
        {
            if (body == null || !body.Value.HasValue)
            {
                throw new AdminException(400, "thieu_gia_tri", "Can truong \"value\".");
            }

            SettingDefinition d = DinhNghia(key);
            JsonNode? moi = JsonNode.Parse(body.Value.Value.GetRawText());
            string? loi = d.KiemGiaTri(moi);
            if (loi != null)
            {
                throw new AdminException(400, "gia_tri_khong_hop_le", "Setting '" + key + "': " + loi);
            }

            string json = moi!.ToJsonString();

            Setting? s = await _db.Settings.FirstOrDefaultAsync(x => x.Key == key, ct);
            if (s == null)
            {
                // Chua khoi tao (khong the xay ra sau khi khoi dong) — tao luon.
                s = Setting.KhoiTao(key, d.DefaultValue.ToJsonString(), _clock.GetUtcNow());
                _db.Settings.Add(s);
            }

            if (JsonNode.DeepEquals(JsonNode.Parse(s.ValueJson), moi))
            {
                return TaoResponse(d, s);
            }

            await KiemRangBuocCapAsync(key, moi, ct);

            string cu = s.ValueJson;
            DateTimeOffset bayGio = _clock.GetUtcNow();
            s.Doi(json, admin, bayGio);
            _db.SettingHistory.Add(SettingHistory.Tao(key, cu, json, s.Version, admin, bayGio, body.Reason));

            _outbox.Enqueue(EventEnvelope.Create(
                Producer,
                Guid.CreateVersion7(),
                new SettingChanged
                {
                    Key = key,
                    Value = RawJson.Tu(json),
                    SettingVersion = s.Version,
                    ChangedBy = admin,
                    ChangedAt = bayGio,
                },
                null,
                new EventActor(admin, ActorRole.Admin)));

            await _db.SaveChangesAsync(ct);
            _store.ApDung(key, json, s.Version);

            _logger.LogInformation("Admin {Admin} doi setting {Key}: {Cu} → {Moi} (v{Version})", admin, key, cu, json, s.Version);
            return TaoResponse(d, s);
        }

        private async Task KiemRangBuocCapAsync(string key, JsonNode moi, CancellationToken ct)
        {
            foreach ((string nho, string lon) in RangBuocCap)
            {
                if (key != nho && key != lon)
                {
                    continue;
                }

                string khac = key == nho ? lon : nho;
                Setting? sKhac = await _db.Settings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == khac, ct);
                double giaTriKhac = SoCua(sKhac == null ? SettingCatalog.Lay(khac).DefaultValue : JsonNode.Parse(sKhac.ValueJson)!);
                double giaTriMoi = SoCua(moi);

                bool sai = key == nho ? giaTriMoi > giaTriKhac : giaTriMoi < giaTriKhac;
                if (sai)
                {
                    throw new AdminException(
                        409,
                        "xung_dot_setting",
                        "'" + nho + "' phai nho hon hoac bang '" + lon + "' (hien " + khac + " = " + giaTriKhac + ").");
                }
            }
        }

        // =====================================================================
        // KHOI TAO + SNAPSHOT
        // =====================================================================

        /// <summary>
        /// Moi khoa trong danh muc chua co trong bang → them voi gia tri khoi tao (kem
        /// lich su "khoi tao"). Khong bao gio ghi de gia tri admin da dat. Development co
        /// the dung gia tri dev rieng (vd treo tien 2 phut de test nhanh).
        /// </summary>
        public async Task<int> KhoiTaoAsync(IReadOnlyDictionary<string, JsonNode> giaTriDev, CancellationToken ct)
        {
            HashSet<string> co = new HashSet<string>(await _db.Settings.Select(s => s.Key).ToListAsync(ct), StringComparer.Ordinal);
            DateTimeOffset bayGio = _clock.GetUtcNow();
            int so = 0;

            foreach (SettingDefinition d in SettingCatalog.TatCa)
            {
                if (co.Contains(d.Key))
                {
                    continue;
                }

                JsonNode? dev;
                JsonNode giaTri = giaTriDev.TryGetValue(d.Key, out dev) ? dev : d.DefaultValue;
                string json = giaTri.ToJsonString();

                _db.Settings.Add(Setting.KhoiTao(d.Key, json, bayGio));
                _db.SettingHistory.Add(SettingHistory.Tao(d.Key, null, json, 1, null, bayGio, "Khoi tao tu danh muc"));
                so++;
            }

            await _db.SaveChangesAsync(ct);
            await NapVaoBoNhoAsync(ct);
            return so;
        }

        public async Task NapVaoBoNhoAsync(CancellationToken ct)
        {
            foreach (Setting s in await _db.Settings.AsNoTracking().ToListAsync(ct))
            {
                _store.ApDung(s.Key, s.ValueJson, s.Version);
            }
        }

        /// <summary>
        /// Xep settings.snapshot (toan bo setting) vao outbox. luu = false khi goi tu
        /// consumer (IdempotencyGuard tu SaveChanges + commit).
        /// </summary>
        public async Task PhatSnapshotAsync(Guid correlationId, Guid? causationId, bool luu, CancellationToken ct)
        {
            List<Setting> ds = await _db.Settings.AsNoTracking().OrderBy(s => s.Key).ToListAsync(ct);
            _outbox.Enqueue(EventEnvelope.Create(
                Producer,
                correlationId,
                new SettingsSnapshot
                {
                    Items = ds.Select(s => new SettingSnapshotItem
                    {
                        Key = s.Key,
                        Value = RawJson.Tu(s.ValueJson),
                        SettingVersion = s.Version,
                    }).ToList(),
                },
                causationId));

            if (luu)
            {
                await _db.SaveChangesAsync(ct);
            }
        }

        // =====================================================================

        private static SettingDefinition DinhNghia(string key)
        {
            SettingDefinition? d = SettingCatalog.Tim(key);
            if (d == null)
            {
                throw new AdminException(404, "khong_tim_thay", "Khong co setting '" + key + "'.");
            }

            return d;
        }

        private static double SoCua(JsonNode n)
        {
            return n.GetValue<double>();
        }

        private static SettingResponse TaoResponse(SettingDefinition d, Setting? s)
        {
            return new SettingResponse
            {
                Key = d.Key,
                Group = d.Group,
                Type = d.TypeName,
                Unit = d.Unit,
                Min = d.Min,
                Max = d.Max,
                Effect = d.EffectName,
                Description = d.Description,
                DefaultValue = d.DefaultValue.DeepClone(),
                Value = s == null ? d.DefaultValue.DeepClone() : JsonNode.Parse(s.ValueJson),
                Version = s == null ? 0 : s.Version,
                UpdatedAt = s == null ? null : s.UpdatedAt,
                UpdatedBy = s == null ? null : s.UpdatedBy,
            };
        }
    }
}
