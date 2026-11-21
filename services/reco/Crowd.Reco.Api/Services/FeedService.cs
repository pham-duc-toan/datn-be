using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Settings;
using Crowd.BuildingBlocks.Storage;
using Crowd.Labeling;
using Crowd.Reco.Api.Dtos;
using Crowd.Reco.Api.Helpers;
using Crowd.Reco.Domain;
using Crowd.Reco.Infrastructure.ClickHouse;
using Crowd.Reco.Infrastructure.Persistence;
using Crowd.Reco.Infrastructure.Redis;
using Crowd.Reco.Infrastructure.Source;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crowd.Reco.Api.Services
{
    /// <summary>
    /// GET /projects/feed — feed cho du an ca nhan hoa (kien truc 3.39 muc 5.2 / 5.6). DUONG NONG: khong truy van DB theo
    /// tung nguoi (tru khi Redis thieu so thich / tap loai tru cua chinh nguoi do).
    ///   trang dau: loc → cham diem → xep lai → luu 200 id (Redis, 15 phut) → tra trang 1;
    ///   trang sau: doc tiep danh sach theo con tro + tron du an MO SAU luc tao danh sach (toi da N / trang).
    /// Danh sach khong bao gio sua, con tro chi di toi, tap "da tra" chan lap.
    /// order=newest (tab "Moi nhat"): CUNG dang the, du an lam duoc ngay xep moi publish truoc, con tro theo (publish, id) —
    /// khong qua mo hinh, khong ghi du lieu hoc, hanh vi khong tinh vao A/B.
    /// </summary>
    public sealed class FeedService
    {
        /// <summary>Phien ban "mo hinh" gan cho hanh vi o tab Moi nhat — ml-svc khong bao gio lay (khong phai mo hinh that).</summary>
        public const string PhienBanMoiNhat = "moi_nhat";

        /// <summary>Tien to ma phien cua tab Moi nhat (frontend gui lai kem luot hien / mo).</summary>
        private const string TienToPhienMoiNhat = "moi_";

        private const int TrangMacDinh = 10;
        private const int TrangToiDa = 30;
        private const int NoiDungToiDa = 4096;
        private const int DoanTrichToiDa = 300;

        private readonly CatalogGoiY _catalog;
        private readonly MoHinhDangChay _moHinh;
        private readonly RecoRedis _redis;
        private readonly NguonDuAn _nguon;
        private readonly RecoDbContext _db;
        private readonly IMediaLinks _media;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;
        private readonly ILogger<FeedService> _logger;

        public FeedService(
            CatalogGoiY catalog,
            MoHinhDangChay moHinh,
            RecoRedis redis,
            NguonDuAn nguon,
            RecoDbContext db,
            IMediaLinks media,
            ISettings settings,
            TimeProvider clock,
            ILogger<FeedService> logger)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (moHinh == null)
            {
                throw new ArgumentNullException(nameof(moHinh));
            }

            if (redis == null)
            {
                throw new ArgumentNullException(nameof(redis));
            }

            if (nguon == null)
            {
                throw new ArgumentNullException(nameof(nguon));
            }

            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (media == null)
            {
                throw new ArgumentNullException(nameof(media));
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

            _catalog = catalog;
            _moHinh = moHinh;
            _redis = redis;
            _nguon = nguon;
            _db = db;
            _media = media;
            _settings = settings;
            _clock = clock;
            _logger = logger;
        }

        public async Task<FeedResponse> LayAsync(ClaimsPrincipal user, string? cursor, int pageSize, CancellationToken ct)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            Guid userId = CrowdClaims.GetUserId(user);
            int n = pageSize < 1 ? TrangMacDinh : Math.Min(pageSize, TrangToiDa);
            DateTimeOffset bayGio = _clock.GetUtcNow();
            ThongSoGoiY thongSo = ThongSo();
            TimeSpan hanPhien = _settings.ThoiGian(SettingKeys.RecoFeedSnapshotTtl);

            LabelerGoiY nguoi = await XayNguoiAsync(userId, user, ct);
            MoHinhDaChon chon = _moHinh.Chon(userId, _settings.SoNguyen(SettingKeys.RecoAbTreatmentPercent));
            AnhCatalog anh = _catalog.Anh;

            ConTro? ct0 = ConTro.Doc(cursor);
            Guid phien;
            int viTri;
            int viTriDau;
            DanhSachLuot? danhSach = null;
            HashSet<Guid> daTra;

            if (ct0 == null)
            {
                phien = Guid.CreateVersion7();
                viTri = 0;
                viTriDau = 0;
                daTra = new HashSet<Guid>();
            }
            else
            {
                phien = ct0.Phien;
                viTri = ct0.ViTri;
                viTriDau = ct0.ViTriThe;
                daTra = await _redis.DocDaTraAsync(userId, phien);
                danhSach = DanhSachLuot.GiaiMa(await _redis.DocDanhSachAsync(userId, phien));
            }

            if (danhSach == null)
            {
                // Lan dau, hoac danh sach het han giua chung: xep moi (tru the da tra trong phien).
                danhSach = XepMoi(anh, nguoi, chon.MoHinh, thongSo, bayGio, daTra, phien);
                viTri = 0;
                await _redis.GhiDanhSachAsync(userId, phien, danhSach.MaHoa(), hanPhien);
            }

            List<TheTrongTrang> the = new List<TheTrongTrang>();
            for (int vong = 0; vong < 2 && the.Count < n; vong++)
            {
                HashSet<Guid> daTraVaTrang = new HashSet<Guid>(daTra);
                foreach (TheTrongTrang x in the)
                {
                    daTraVaTrang.Add(x.DuAn.Id);
                }

                DanhSachLuot ds = danhSach;
                List<DuAnDaCham> moi = anh.DuAn.Values
                    .Where(p => p.OpenedAt > ds.TaoLuc && !daTraVaTrang.Contains(p.Id) && DieuKienGoiY.LamDuoc(p, nguoi))
                    .Select(p => ChamDiem.Cham(p, nguoi, chon.MoHinh, anh.ThongKe, thongSo, bayGio))
                    .ToList();

                KetQuaTrang kq = GhepTrang.Ghep(
                    danhSach,
                    viTri,
                    n - the.Count,
                    daTraVaTrang,
                    id => TimDuAn(anh, id),
                    p => DieuKienGoiY.LamDuoc(p, nguoi),
                    vong == 0 ? moi : new List<DuAnDaCham>(),
                    thongSo,
                    viTriDau + the.Count);
                the.AddRange(kq.The);
                viTri = kq.ConTro;

                if (!kq.HetDanhSach || the.Count >= n)
                {
                    break;
                }

                // Het danh sach ma trang chua du: xep lai phan con lai (tru moi the da tra) va ghep tiep MOT lan.
                foreach (TheTrongTrang x in kq.The)
                {
                    daTraVaTrang.Add(x.DuAn.Id);
                }

                danhSach = XepMoi(anh, nguoi, chon.MoHinh, thongSo, bayGio, daTraVaTrang, phien);
                viTri = 0;
                await _redis.GhiDanhSachAsync(userId, phien, danhSach.MaHoa(), hanPhien);
                if (danhSach.Muc.Count == 0)
                {
                    break;
                }
            }

            string maTrang = Guid.CreateVersion7().ToString("N");
            List<FeedCard> the2 = new List<FeedCard>();
            foreach (TheTrongTrang x in the)
            {
                DuAnDaCham daCham = ChamDiem.Cham(x.DuAn, nguoi, chon.MoHinh, anh.ThongKe, thongSo, bayGio);
                the2.Add(TaoThe(x.DuAn, x.ViTri, x.KhamPha, daCham, anh, userId));
                // Du lieu hoc: dac trung LUC TRA + vi tri the (vi tri chi la dac trung luc hoc, luc xep = 0).
                double[] dacTrung = (double[])daCham.DacTrung.Clone();
                dacTrung[DacTrung.Position] = x.ViTri;
                await _redis.GhiHanhViAsync(HanhViService.MaHoa(new MucHangDoi
                {
                    Sv = new DongDaTra
                    {
                        EventId = Guid.CreateVersion7(),
                        At = bayGio,
                        RequestId = maTrang,
                        UserId = userId,
                        ProjectId = x.DuAn.Id,
                        Position = x.ViTri,
                        SessionId = phien.ToString("N"),
                        ModelVersion = chon.MoHinh.PhienBan,
                        AbGroup = chon.Nhom,
                        Score = daCham.Diem,
                        PStart = daCham.PBatDau,
                        PPass = daCham.PDat,
                        Features = dacTrung,
                        Explore = x.KhamPha,
                    },
                }));
            }

            await _redis.ThemDaTraAsync(userId, phien, the.Select(x => x.DuAn.Id), hanPhien);

            int viTriKe = viTriDau + the.Count;
            return new FeedResponse
            {
                Items = the2,
                NextCursor = the.Count == 0 ? null : new ConTro { Phien = phien, ViTri = viTri, ViTriThe = viTriKe }.Viet(),
                RequestId = maTrang,
                SessionId = phien.ToString("N"),
            };
        }

        /// <summary>Ma phien thuoc tab Moi nhat.</summary>
        public static bool LaPhienMoiNhat(string? sessionId)
        {
            return sessionId != null && sessionId.StartsWith(TienToPhienMoiNhat, StringComparison.Ordinal);
        }

        /// <summary>
        /// GET /projects/feed?order=newest — du an DANG NHAN BAI ma nguoi nay lam duoc ngay, publish moi nhat truoc. Con tro =
        /// (luc publish, id) cua the cuoi: trang sau chi lay du an CU HON nen khong lap; du an mo sau khi bat dau luot hien o
        /// dau danh sach khi lam moi.
        /// </summary>
        public async Task<FeedResponse> LayMoiNhatAsync(ClaimsPrincipal user, string? cursor, int pageSize, CancellationToken ct)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            Guid userId = CrowdClaims.GetUserId(user);
            int n = pageSize < 1 ? TrangMacDinh : Math.Min(pageSize, TrangToiDa);
            DateTimeOffset bayGio = _clock.GetUtcNow();
            ThongSoGoiY thongSo = ThongSo();
            LabelerGoiY nguoi = await XayNguoiAsync(userId, user, ct);
            MoHinhDaChon chon = _moHinh.Chon(userId, _settings.SoNguyen(SettingKeys.RecoAbTreatmentPercent));
            AnhCatalog anh = _catalog.Anh;

            ConTroMoiNhat? c = ConTroMoiNhat.Doc(cursor);
            Guid phien = c == null ? Guid.CreateVersion7() : c.Phien;
            int viTriDau = c == null ? 0 : c.ViTriThe;

            List<DuAnGoiY> trang = anh.DuAn.Values
                .Where(x => DieuKienGoiY.LamDuoc(x, nguoi))
                .Where(x => c == null || x.PublishedAt < c.PublishedAt || (x.PublishedAt == c.PublishedAt && x.Id.CompareTo(c.Id) < 0))
                .OrderByDescending(x => x.PublishedAt)
                .ThenByDescending(x => x.Id)
                .Take(n + 1)
                .ToList();
            bool conNua = trang.Count > n;
            if (conNua)
            {
                trang.RemoveAt(n);
            }

            List<FeedCard> the = new List<FeedCard>();
            for (int i = 0; i < trang.Count; i++)
            {
                DuAnGoiY x = trang[i];
                DuAnDaCham daCham = ChamDiem.Cham(x, nguoi, chon.MoHinh, anh.ThongKe, thongSo, bayGio);
                the.Add(TaoThe(x, viTriDau + i, false, daCham, anh, userId));
            }

            string? tiep = null;
            if (conNua && trang.Count > 0)
            {
                DuAnGoiY cuoi = trang[trang.Count - 1];
                tiep = new ConTroMoiNhat { Phien = phien, PublishedAt = cuoi.PublishedAt, Id = cuoi.Id, ViTriThe = viTriDau + trang.Count }.Viet();
            }

            return new FeedResponse
            {
                Items = the,
                NextCursor = tiep,
                RequestId = Guid.CreateVersion7().ToString("N"),
                SessionId = TienToPhienMoiNhat + phien.ToString("N"),
            };
        }

        private ThongSoGoiY ThongSo()
        {
            return new ThongSoGoiY
            {
                ChuKyQuenNgay = _settings.SoNguyen(SettingKeys.RecoAffinityHalfLifeDays),
                NguongDuAnMoi = _settings.SoNguyen(SettingKeys.RecoNewProjectExposure),
                KhamPhaMoi = _settings.SoNguyen(SettingKeys.RecoExploreEvery),
                ToiDaMotDoanhNghiep = _settings.SoNguyen(SettingKeys.RecoMaxPerBusinessTop),
                ChenMoiMoiTrang = _settings.SoNguyen(SettingKeys.RecoNewInsertsPerPage),
                KichThuocDanhSach = 200,
            };
        }

        private static DuAnGoiY? TimDuAn(AnhCatalog anh, Guid id)
        {
            DuAnGoiY? p;
            return anh.DuAn.TryGetValue(id, out p) ? p : null;
        }

        private static DanhSachLuot XepMoi(
            AnhCatalog anh, LabelerGoiY nguoi, IMoHinhGoiY moHinh, ThongSoGoiY thongSo, DateTimeOffset bayGio, HashSet<Guid> boQua, Guid phien)
        {
            List<DuAnDaCham> daCham = anh.DuAn.Values
                .Where(p => !boQua.Contains(p.Id) && DieuKienGoiY.LamDuoc(p, nguoi))
                .Select(p => ChamDiem.Cham(p, nguoi, moHinh, anh.ThongKe, thongSo, bayGio))
                .ToList();

            // Ngau nhien cua o kham pha: co dinh theo (nguoi, phien) — cung phien xep lai van ra cung thu tu.
            int hat = BitConverter.ToInt32(phien.ToByteArray(), 0) ^ BitConverter.ToInt32(nguoi.UserId.ToByteArray(), 4);
            List<DuAnDaCham> xep = XepLai.Xep(daCham, nguoi, thongSo, new Random(hat));
            return DanhSachLuot.Tu(xep, bayGio);
        }

        /// <summary>
        /// Thong tin labeler: claim token (cap do, uy tin, xac minh, du tuoi) + Redis (so thich, tap loai tru). Redis thieu
        /// thi nap tu nguon goc MOT lan roi ghi lai; nguon goc loi thi dung tap rong (khong luu) — feed van tra duoc.
        /// </summary>
        private async Task<LabelerGoiY> XayNguoiAsync(Guid userId, ClaimsPrincipal user, CancellationToken ct)
        {
            SoThich? st = await _redis.DocSoThichAsync(userId);
            if (st == null)
            {
                st = await BoDemStore.DocAsync(_db, userId, ct);
                await _redis.GhiSoThichAsync(userId, st, _settings.ThoiGian(SettingKeys.RecoAffinityCacheTtl));
            }

            TapLoaiTru? ex = await _redis.DocLoaiTruAsync(userId);
            if (ex == null)
            {
                try
                {
                    LoaiTruCuaNguoi goc = await _nguon.DocLoaiTruAsync(userId, ct);
                    List<Guid> an = await _db.HiddenProjects.AsNoTracking().Where(x => x.UserId == userId).Select(x => x.ProjectId).ToListAsync(ct);
                    List<string> truong = new List<string>();
                    truong.AddRange(goc.DaNop.Select(RecoRedis.TruongDaNop));
                    truong.AddRange(goc.BiChan.Select(RecoRedis.TruongBiChan));
                    truong.AddRange(goc.ThanhVien.Select(RecoRedis.TruongThanhVien));
                    truong.AddRange(goc.DoanhNghiepChan.Select(RecoRedis.TruongDoanhNghiepChan));
                    truong.AddRange(an.Select(RecoRedis.TruongKhongQuanTam));
                    await _redis.GhiLoaiTruAsync(userId, truong, TimeSpan.FromDays(7));

                    HashSet<Guid> loaiTru = new HashSet<Guid>(goc.DaNop);
                    loaiTru.UnionWith(goc.BiChan);
                    loaiTru.UnionWith(an);
                    ex = new TapLoaiTru { LoaiTru = loaiTru, ThanhVien = goc.ThanhVien, DoanhNghiepChan = goc.DoanhNghiepChan };
                }
                catch (Exception e) when (e is Npgsql.NpgsqlException || e is TimeoutException)
                {
                    _logger.LogWarning(e, "Khong doc duoc tap loai tru tu project-svc — tam dung tap rong (khong luu)");
                    ex = new TapLoaiTru { LoaiTru = new HashSet<Guid>(), ThanhVien = new HashSet<Guid>(), DoanhNghiepChan = new HashSet<Guid>() };
                }
            }

            return new LabelerGoiY
            {
                UserId = userId,
                Level = CrowdClaims.CapDo(user),
                Reputation = CrowdClaims.UyTin(user),
                IdentityVerified = CrowdClaims.DaXacMinhDanhTinh(user),
                Adult = CrowdClaims.DuTuoi(user),
                SoThich = st,
                LoaiTru = ex.LoaiTru,
                ThanhVien = ex.ThanhVien,
                DoanhNghiepChan = ex.DoanhNghiepChan,
            };
        }

        private FeedCard TaoThe(DuAnGoiY p, int viTri, bool khamPha, DuAnDaCham daCham, AnhCatalog anh, Guid userId)
        {
            List<string> lyDo = new List<string>(daCham.LyDo);
            if (khamPha && !lyDo.Contains(ChamDiem.LyDoKhamPha))
            {
                lyDo.Add(ChamDiem.LyDoKhamPha);
            }

            return new FeedCard
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                Modality = p.Modality,
                RewardVnd = p.RewardVnd,
                Slots = p.Slots,
                QuestionCount = p.QuestionCount,
                EstimatedMinutes = (int)Math.Max(1, Math.Ceiling(daCham.DacTrung[DacTrung.EstMinutes])),
                Deadline = p.Deadline,
                ToolKinds = p.ToolKinds,
                Domains = p.Domains,
                Languages = p.Languages,
                RequireEntranceTest = p.RequireEntranceTest,
                SensitiveContent = p.SensitiveContent,
                ConsentRequired = p.SensitiveContent || p.NdaRequired,
                RequireVerifiedIdentity = p.RequireVerifiedIdentity,
                IsPrivate = p.IsPrivate,
                IsNew = daCham.DacTrung[DacTrung.IsNew] > 0,
                Explore = khamPha,
                Position = viTri,
                Reasons = lyDo,
                Tools = p.Tools.Select(t => new FeedTool { Name = t.Name, Kind = t.Kind, Question = t.Question, Classes = t.Classes }).ToList(),
                Previews = XemTruoc(p, anh, userId),
            };
        }

        /// <summary>Mau xem truoc (P1, toi da MOT mau): file ky link qua media-svc (watermark mang ma nguoi xem), van ban tra noi dung.</summary>
        private List<FeedPreview> XemTruoc(DuAnGoiY p, AnhCatalog anh, Guid userId)
        {
            List<FeedPreview> ds = new List<FeedPreview>();
            IReadOnlyList<MauXemTruocRam>? mau;
            if (!anh.MauXemTruoc.TryGetValue(p.Id, out mau))
            {
                return ds;
            }

            string? tenDn;
            anh.TenDoanhNghiep.TryGetValue(p.OwnerId, out tenDn);
            NguoiXem nguoi = NguoiXem.Labeler(userId);
            foreach (MauXemTruocRam m in mau)
            {
                string? fileUrl = null;
                string? chuNoi = null;
                RawJson metadata = RawJson.Tu(string.IsNullOrWhiteSpace(m.MetadataJson) ? "{}" : m.MetadataJson);
                if (m.StorageKey != null)
                {
                    double? batDau = null;
                    double? ketThuc = null;
                    if (m.Modality == Modalities.Video)
                    {
                        SampleMetadata md = SampleMetadata.Tu(metadata);
                        batDau = md.SegmentStart ?? 0;
                        ketThuc = md.SegmentEnd ?? md.DurationSec;
                    }

                    MauCanXem mx = new MauCanXem
                    {
                        SampleId = m.SampleId,
                        ProjectId = m.ProjectId,
                        StorageKey = m.StorageKey,
                        Modality = m.Modality,
                        TenDoanhNghiep = tenDn,
                        BatDau = batDau,
                        KetThuc = ketThuc,
                    };
                    fileUrl = _media.TaoLink(mx, nguoi, CheDoXem.Watermark);
                    chuNoi = _media.ChuDeLenTrinhPhat(mx, nguoi, CheDoXem.Watermark);
                }

                RawJson? noiDung = null;
                string? doanTrich = null;
                if (m.ContentJson != null)
                {
                    if (Encoding.UTF8.GetByteCount(m.ContentJson) <= NoiDungToiDa)
                    {
                        noiDung = RawJson.Tu(m.ContentJson);
                    }

                    doanTrich = DoanTrich(m.ContentJson);
                }

                ds.Add(new FeedPreview
                {
                    SampleId = m.SampleId,
                    Modality = m.Modality,
                    FileUrl = fileUrl,
                    WatermarkText = chuNoi,
                    Content = noiDung,
                    Snippet = doanTrich,
                    Question = m.Question,
                    Metadata = metadata,
                });
            }

            return ds;
        }

        private static string? DoanTrich(string json)
        {
            try
            {
                using (JsonDocument d = JsonDocument.Parse(json))
                {
                    JsonElement t;
                    if (d.RootElement.ValueKind == JsonValueKind.Object && d.RootElement.TryGetProperty("text", out t) && t.ValueKind == JsonValueKind.String)
                    {
                        string s = t.GetString() ?? string.Empty;
                        return s.Length <= DoanTrichToiDa ? s : string.Concat(s.AsSpan(0, DoanTrichToiDa), "…");
                    }
                }
            }
            catch (JsonException)
            {
                return null;
            }

            return null;
        }
    }

    /// <summary>Con tro trang: phien + vi tri trong danh sach + so the da tra (vi tri the ke tiep). base64url "phien.viTri.viTriThe".</summary>
    public sealed class ConTro
    {
        public required Guid Phien { get; init; }

        public required int ViTri { get; init; }

        public required int ViTriThe { get; init; }

        public string Viet()
        {
            string s = Phien.ToString("N") + "." + ViTri.ToString(System.Globalization.CultureInfo.InvariantCulture) + "."
                       + ViTriThe.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Convert.ToBase64String(Encoding.ASCII.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static ConTro? Doc(string? s)
        {
            if (string.IsNullOrWhiteSpace(s) || s.Length > 200)
            {
                return null;
            }

            try
            {
                string b = s.Replace('-', '+').Replace('_', '/');
                while (b.Length % 4 != 0)
                {
                    b += "=";
                }

                string[] p = Encoding.ASCII.GetString(Convert.FromBase64String(b)).Split('.');
                Guid phien;
                int viTri;
                int viTriThe;
                if (p.Length == 3 && Guid.TryParse(p[0], out phien)
                    && int.TryParse(p[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out viTri)
                    && int.TryParse(p[2], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out viTriThe)
                    && viTri >= 0 && viTriThe >= 0)
                {
                    return new ConTro { Phien = phien, ViTri = viTri, ViTriThe = viTriThe };
                }
            }
            catch (FormatException)
            {
                return null;
            }

            return null;
        }
    }

    /// <summary>Con tro tab Moi nhat: phien + (luc publish, id) cua the cuoi + vi tri the ke tiep. base64url.</summary>
    public sealed class ConTroMoiNhat
    {
        public required Guid Phien { get; init; }

        public required DateTimeOffset PublishedAt { get; init; }

        public required Guid Id { get; init; }

        public required int ViTriThe { get; init; }

        public string Viet()
        {
            string s = "n." + Phien.ToString("N") + "." + PublishedAt.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture) + "."
                       + Id.ToString("N") + "." + ViTriThe.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Convert.ToBase64String(Encoding.ASCII.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static ConTroMoiNhat? Doc(string? s)
        {
            if (string.IsNullOrWhiteSpace(s) || s.Length > 200)
            {
                return null;
            }

            try
            {
                string b = s.Replace('-', '+').Replace('_', '/');
                while (b.Length % 4 != 0)
                {
                    b += "=";
                }

                string[] p = Encoding.ASCII.GetString(Convert.FromBase64String(b)).Split('.');
                Guid phien;
                long ticks;
                Guid id;
                int viTriThe;
                if (p.Length == 5 && p[0] == "n" && Guid.TryParse(p[1], out phien)
                    && long.TryParse(p[2], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out ticks)
                    && Guid.TryParse(p[3], out id)
                    && int.TryParse(p[4], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out viTriThe)
                    && ticks >= DateTimeOffset.MinValue.UtcTicks && ticks <= DateTimeOffset.MaxValue.UtcTicks && viTriThe >= 0)
                {
                    return new ConTroMoiNhat { Phien = phien, PublishedAt = new DateTimeOffset(ticks, TimeSpan.Zero), Id = id, ViTriThe = viTriThe };
                }
            }
            catch (FormatException)
            {
                return null;
            }

            return null;
        }
    }
}
