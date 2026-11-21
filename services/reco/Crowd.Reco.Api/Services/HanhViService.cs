using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Settings;
using Crowd.Reco.Api.Dtos;
using Crowd.Reco.Api.Helpers;
using Crowd.Reco.Domain;
using Crowd.Reco.Infrastructure.ClickHouse;
using Crowd.Reco.Infrastructure.Persistence;
using Crowd.Reco.Infrastructure.Redis;
using Crowd.Reco.Infrastructure.Source;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Reco.Api.Services
{
    /// <summary>Mot muc trong Redis Stream reco:events: hanh vi (Ev) hoac the da tra kem dac trung (Sv).</summary>
    public sealed class MucHangDoi
    {
        public DongHanhVi? Ev { get; set; }

        public DongDaTra? Sv { get; set; }

        /// <summary>Hanh vi tu frontend — relay cong bo dem theo lo (hanh vi tu event da duoc consumer cong trong transaction).</summary>
        public bool TuFrontend { get; set; }
    }

    /// <summary>
    /// Ghi hanh vi cua labeler (kien truc 3.39 muc 4.2 / 4.3):
    ///   - frontend gui luot hien / mo / "khong quan tam" theo lo → Redis Stream (relay ghi ClickHouse + cong bo dem theo lo);
    ///   - consumer event (bat dau / nop / dat / truot / bo do) cong bo dem NGAY trong transaction cua consumer.
    /// Duong request chi cham Redis (va Postgres voi "khong quan tam" — it, can ben).
    /// </summary>
    public sealed class HanhViService
    {
        public const int ToiDaMoiLo = 100;

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        private readonly RecoRedis _redis;
        private readonly CatalogGoiY _catalog;
        private readonly NguonDuAn _nguon;
        private readonly RecoDbContext _db;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;
        private readonly MoHinhDangChay _moHinh;

        public HanhViService(
            RecoRedis redis, CatalogGoiY catalog, NguonDuAn nguon, RecoDbContext db, ISettings settings, TimeProvider clock, MoHinhDangChay moHinh)
        {
            if (moHinh == null)
            {
                throw new ArgumentNullException(nameof(moHinh));
            }

            _moHinh = moHinh;

            if (redis == null)
            {
                throw new ArgumentNullException(nameof(redis));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (nguon == null)
            {
                throw new ArgumentNullException(nameof(nguon));
            }

            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _redis = redis;
            _catalog = catalog;
            _nguon = nguon;
            _db = db;
            _settings = settings;
            _clock = clock;
        }

        public static string MaHoa(MucHangDoi m)
        {
            return JsonSerializer.Serialize(m, Json);
        }

        public static MucHangDoi? GiaiMa(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<MucHangDoi>(json, Json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>POST /reco/events — luot hien / mo / khong quan tam. Sai loai / thieu du an → bo qua luot do.</summary>
        public async Task<int> GhiTuFrontendAsync(Guid userId, RecoEventsRequest body, CancellationToken ct)
        {
            if (body == null || body.Events == null)
            {
                return 0;
            }

            if (body.Events.Count > ToiDaMoiLo)
            {
                throw new RecoException(400, "qua_nhieu_luot", "Mỗi lần gửi tối đa " + ToiDaMoiLo + " lượt.");
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            // Nhom A/B + mo hinh cua nguoi nay LUC NAY — ml-svc so ti le "thay → bat dau" giua hai nhom.
            MoHinhDaChon chon = _moHinh.Chon(userId, _settings.SoNguyen(SettingKeys.RecoAbTreatmentPercent));
            int nhan = 0;
            foreach (RecoEventItem e in body.Events)
            {
                if (e == null || e.Kind == null || !HanhVi.TuFrontend.Contains(e.Kind) || e.ProjectId == Guid.Empty)
                {
                    continue;
                }

                DongHanhVi dong = new DongHanhVi
                {
                    EventId = Guid.CreateVersion7(),
                    At = bayGio,
                    UserId = userId,
                    ProjectId = e.ProjectId,
                    Kind = e.Kind,
                    Position = e.Position.HasValue ? Math.Clamp(e.Position.Value, -1, 100000) : -1,
                    SessionId = CatChuoi(e.SessionId, 64),
                    RequestId = CatChuoi(e.RequestId, 64),
                    // Tab "Moi nhat" khong do mo hinh xep → khong tinh vao A/B (ml-svc chi lay dung phien ban mo hinh).
                    ModelVersion = FeedService.LaPhienMoiNhat(e.SessionId) ? FeedService.PhienBanMoiNhat : chon.MoHinh.PhienBan,
                    AbGroup = chon.Nhom,
                };

                if (e.Kind == HanhVi.HienThe)
                {
                    // Be du an moi dem nguoi THAT SU thay the (HyperLogLog — 1 nguoi dem 1 lan).
                    await _redis.GhiDaThayAsync(e.ProjectId, userId);
                }
                else if (e.Kind == HanhVi.KhongQuanTam)
                {
                    await AnDuAnAsync(userId, e.ProjectId, bayGio, ct);
                }

                await _redis.GhiHanhViAsync(MaHoa(new MucHangDoi { Ev = dong, TuFrontend = true }));
                nhan++;
            }

            return nhan;
        }

        /// <summary>"Khong quan tam": ghi ben (reco_db) + them vao tap loai tru ngay — the bien mat tu trang sau.</summary>
        private async Task AnDuAnAsync(Guid userId, Guid projectId, DateTimeOffset luc, CancellationToken ct)
        {
            bool daCo = await _db.HiddenProjects.AnyAsync(x => x.UserId == userId && x.ProjectId == projectId, ct);
            if (!daCo)
            {
                _db.HiddenProjects.Add(new HiddenProject { UserId = userId, ProjectId = projectId, CreatedAt = luc });
                try
                {
                    await _db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    // Bam hai lan cung luc — dong kia da ghi.
                    _db.ChangeTracker.Clear();
                }
            }

            await _redis.DatLoaiTruAsync(userId, RecoRedis.TruongKhongQuanTam(projectId));
        }

        /// <summary>
        /// Consumer: cong bo dem cho hanh vi tren du an (TRONG transaction cua consumer — cung processed_events), roi bo
        /// cache so thich va day hanh vi vao stream (ClickHouse). Du an khong tim thay → chi ghi hanh vi.
        /// </summary>
        public async Task GhiTuEventAsync(Guid eventId, Guid userId, Guid projectId, string hanhVi, DateTimeOffset luc, CancellationToken ct)
        {
            DuAnGoiY? duAn = await TimDuAnAsync(projectId, ct);
            if (duAn != null)
            {
                List<CongBoDem> thayDoi = HanhVi.ThayDoi(hanhVi, duAn)
                    .Select(x => new CongBoDem { UserId = userId, Loai = x.Loai, Ma = x.Ma, WA = x.WA, WB = x.WB })
                    .ToList();
                await BoDemStore.CongAsync(_db, thayDoi, luc, _settings.SoNguyen(SettingKeys.RecoAffinityHalfLifeDays), ct);
                await _redis.XoaSoThichAsync(new Guid[] { userId });
            }

            await _redis.GhiHanhViAsync(MaHoa(new MucHangDoi
            {
                Ev = new DongHanhVi { EventId = eventId, At = luc, UserId = userId, ProjectId = projectId, Kind = hanhVi },
                TuFrontend = false,
            }));
        }

        /// <summary>The cua du an: tu bo nho (dang nhan bai) hoac doc view (du an da dong — consumer cham bai).</summary>
        public async Task<DuAnGoiY?> TimDuAnAsync(Guid projectId, CancellationToken ct)
        {
            DuAnGoiY? p = _catalog.Tim(projectId);
            if (p != null)
            {
                return p;
            }

            DongDuAn? d = await _nguon.DocMotAsync(projectId, ct);
            if (d == null)
            {
                return null;
            }

            return new DuAnGoiY
            {
                Id = d.Id,
                OwnerId = d.OwnerId,
                Name = d.Name,
                Description = d.Description,
                Modality = d.Modality,
                Domains = d.Domains,
                Languages = d.Languages,
                ToolKinds = d.ToolKinds,
                RewardVnd = d.RewardVnd,
                Slots = d.Slots,
                QuestionCount = d.QuestionCount,
                Deadline = d.Deadline,
                PublishedAt = d.PublishedAt,
                IsPrivate = d.IsPrivate,
                MinLevel = d.MinLevel,
                MinReputation = d.MinReputation,
                RequireEntranceTest = d.RequireEntranceTest,
                RequireVerifiedIdentity = d.RequireVerifiedIdentity,
                SensitiveContent = d.SensitiveContent,
                NdaRequired = d.NdaRequired,
                Accepting = d.Accepting,
                SubmittedCount = d.SubmittedCount,
                InProgressCount = d.InProgressCount,
                StartedCount = d.StartedCount,
                MedianSecondsPerQuestion = d.MedianSecondsPerQuestion,
                Tools = d.Tools,
                OpenedAt = d.PublishedAt,
            };
        }

        private static string CatChuoi(string? s, int toiDa)
        {
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }

            return s.Length <= toiDa ? s : s.Substring(0, toiDa);
        }
    }
}
