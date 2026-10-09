using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Security;
using Crowd.BuildingBlocks.Settings;
using Crowd.BuildingBlocks.Storage;
using Crowd.Contracts.Gate;
using Crowd.Gate.Api.Dtos;
using Crowd.Gate.Api.Helpers;
using Crowd.Gate.Domain;
using Crowd.Gate.Infrastructure.ClickHouse;
using Crowd.Gate.Infrastructure.Redis;
using Crowd.Labeling;
using Crowd.Settings;
using Microsoft.Extensions.Logging;

namespace Crowd.Gate.Api.Services
{
    /// <summary>Thong tin khach vang lai cua mot request (controller dung tu HttpContext).</summary>
    public sealed class KhachVangLai
    {
        public required string Ip { get; init; }

        public required string IpHash { get; init; }

        public required string IpHashNgay { get; init; }

        public required string ReferrerHost { get; init; }

        /// <summary>Co token dang nhap thi la userId — de bat chu link tu vuot.</summary>
        public Guid? UserId { get; init; }
    }

    /// <summary>Mot muc trong Redis Stream gate:events — relay chuyen sang outbox + ClickHouse.</summary>
    public sealed class MucSuKien
    {
        public DongClick Row { get; set; } = new DongClick();

        public GateSolved? Solved { get; set; }

        public ClickValidated? Click { get; set; }
    }

    /// <summary>
    /// Trang vuot link (dac ta 2.4, docs 3.6). DUONG NONG: chi doc GateCatalog (bo nho) va
    /// Redis; ket qua (nhan, tien, thong ke) ghi vao Redis Stream va relay day di sau —
    /// khong cho broker hay database trong request.
    ///
    ///   GET  /g/{code}                    thong tin trang
    ///   POST /g/{code}/sessions           Turnstile + mat khau → bo k cau vang + m cau that
    ///   POST /g/sessions/{id}/submit      cham CHI cau vang → dat: token mot lan / truot: bo moi
    ///   GET  /go/{code}?t=token           GETDEL jti → 302 toi link dich
    /// </summary>
    public sealed class GateService
    {
        private readonly GateCatalog _catalog;
        private readonly RedisGateStore _redis;
        private readonly IObjectStorage _storage;
        private readonly TurnstileVerifier _turnstile;
        private readonly GateTokenService _token;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;
        private readonly ILogger<GateService> _logger;

        public GateService(
            GateCatalog catalog,
            RedisGateStore redis,
            IObjectStorage storage,
            TurnstileVerifier turnstile,
            GateTokenService token,
            ISettings settings,
            TimeProvider clock,
            ILogger<GateService> logger)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (redis == null)
            {
                throw new ArgumentNullException(nameof(redis));
            }

            if (storage == null)
            {
                throw new ArgumentNullException(nameof(storage));
            }

            if (turnstile == null)
            {
                throw new ArgumentNullException(nameof(turnstile));
            }

            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
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
            _redis = redis;
            _storage = storage;
            _turnstile = turnstile;
            _token = token;
            _settings = settings;
            _clock = clock;
            _logger = logger;
        }

        // =====================================================================
        // BUOC 0: THONG TIN TRANG
        // =====================================================================

        public GatePageResponse Trang(string code)
        {
            LinkCong l = LayLinkPhucVu(code);
            return new GatePageResponse
            {
                Code = l.Code,
                RequiresPassword = l.PasswordHash != null,
                CountdownSeconds = (int)Math.Ceiling(_settings.ThoiGian(SettingKeys.GateCountdown).TotalSeconds),
                TurnstileEnabled = _turnstile.BatBuoc,
                TurnstileSiteKey = _turnstile.SiteKey,
            };
        }

        // =====================================================================
        // BUOC 1-2: PHAT BO CAU HOI
        // =====================================================================

        public async Task<GateSessionResponse> TaoPhienAsync(string code, CreateSessionRequest? body, KhachVangLai khach, CancellationToken ct)
        {
            if (khach == null)
            {
                throw new ArgumentNullException(nameof(khach));
            }

            LinkCong l = LayLinkPhucVu(code);

            if (l.PasswordHash != null)
            {
                string? matKhau = body == null ? null : body.Password;
                if (matKhau == null || !MatKhauLink.Kiem(matKhau, l.PasswordHash))
                {
                    throw new GateException(403, "sai_mat_khau", "Mat khau link khong dung.");
                }
            }

            // Turnstile TRUOC khi phat bat cu cau nao: bot re tien chet o day, khong ton cau that (VD-L-06).
            if (!await _turnstile.KiemAsync(body == null ? null : body.TurnstileToken, khach.Ip, ct))
            {
                throw new GateException(403, "turnstile_that_bai", "Xac minh chong bot that bai. Tai lai trang va thu lai.");
            }

            PhienVuot p = await PhatPhienAsync(l, khach);

            await GhiAsync(new MucSuKien
            {
                Row = new DongClick
                {
                    ClickId = p.SessionId,
                    At = p.CreatedAt,
                    Kind = "view",
                    LinkId = l.LinkId,
                    OwnerId = l.OwnerId,
                    CampaignId = l.CampaignId,
                    ProjectId = p.ProjectId,
                    IpHashDay = khach.IpHashNgay,
                    ReferrerHost = khach.ReferrerHost,
                },
            });

            return await TaoResponseAsync(p);
        }

        private async Task<PhienVuot> PhatPhienAsync(LinkCong l, KhachVangLai khach)
        {
            DateTimeOffset bayGio = _clock.GetUtcNow();
            PhienVuot p = new PhienVuot
            {
                SessionId = Guid.CreateVersion7(),
                LinkId = l.LinkId,
                Code = l.Code,
                OwnerId = l.OwnerId,
                CampaignId = l.CampaignId,
                CreatedAt = bayGio,
                AnswerableAt = bayGio + _settings.ThoiGian(SettingKeys.GateCountdown),
                IpHash = khach.IpHash,
                IpHashDay = khach.IpHashNgay,
                ReferrerHost = khach.ReferrerHost,
            };

            if (_settings.DungSai(SettingKeys.GateEnabled))
            {
                await ChonCauHoiAsync(p);
            }

            await _redis.LuuPhienAsync(p, _settings.ThoiGian(SettingKeys.GateSessionTtl));
            return p;
        }

        /// <summary>
        /// Chon du an (ngau nhien trong cac du an con ngan sach) va k cau vang + m cau that.
        /// Mau da nhan du gate.max_labels_per_sample nhan cong link thi khong phat nua.
        /// </summary>
        private async Task ChonCauHoiAsync(PhienVuot p)
        {
            int soVang = _settings.SoNguyen(SettingKeys.GateGoldPerSession);
            int soThat = _settings.SoNguyen(SettingKeys.GateRealPerSession);
            int tran = _settings.SoNguyen(SettingKeys.GateMaxLabelsPerSample);

            foreach (DuAnPhucVu d in _catalog.UngVien(soThat, soVang))
            {
                // Lay du ung vien (gap doi) roi loc theo bo dem Redis — mot lan MGET.
                List<Guid> ungVien = d.CauThat.OrderBy(x => Random.Shared.Next()).Take(soThat * 4).ToList();
                long[] dem = await _redis.DemNhanMauAsync(ungVien);
                List<Guid> that = new List<Guid>();
                for (int i = 0; i < ungVien.Count && that.Count < soThat; i++)
                {
                    if (dem[i] < tran)
                    {
                        that.Add(ungVien[i]);
                    }
                }

                if (that.Count == 0)
                {
                    continue;
                }

                p.ProjectId = d.DuAn.ProjectId;
                p.RealIds = that;
                p.GoldIds = d.CauVang.OrderBy(x => Random.Shared.Next()).Take(soVang).ToList();
                return;
            }
        }

        private async Task<GateSessionResponse> TaoResponseAsync(PhienVuot p)
        {
            List<GateQuestion> cau = new List<GateQuestion>();
            JsonElement? tapNhan = null;

            if (p.ProjectId.HasValue)
            {
                DuAnPhucVu? d = _catalog.LayDuAn(p.ProjectId.Value);
                if (d != null)
                {
                    tapNhan = d.TapNhanJson;
                }

                // Tron vang + that: client khong phan biet duoc cau nao duoc cham.
                foreach (Guid id in p.GoldIds.Concat(p.RealIds).OrderBy(x => Random.Shared.Next()))
                {
                    MauCong? m = _catalog.LayMau(id);
                    if (m == null)
                    {
                        continue;
                    }

                    cau.Add(new GateQuestion
                    {
                        SampleId = m.SampleId,
                        Modality = m.Modality,
                        FileUrl = m.StorageKey == null ? null : await _storage.TaoLinkXemAsync(m.StorageKey),
                        Content = m.Content == null ? null : m.Content.Element(),
                        Metadata = m.Metadata.Element(),
                    });
                }
            }

            return new GateSessionResponse
            {
                SessionId = p.SessionId,
                AnswerableAt = p.AnswerableAt,
                ExpiresAt = p.CreatedAt + _settings.ThoiGian(SettingKeys.GateSessionTtl),
                LabelSchema = tapNhan,
                Questions = cau,
            };
        }

        // =====================================================================
        // BUOC 3: CHAM CAU VANG
        // =====================================================================

        public async Task<SubmitResponse> NopAsync(Guid sessionId, SubmitRequest? body, KhachVangLai khach, CancellationToken ct)
        {
            if (khach == null)
            {
                throw new ArgumentNullException(nameof(khach));
            }

            PhienVuot? p = await _redis.DocPhienAsync(sessionId);
            if (p == null)
            {
                throw new GateException(410, "phien_het_han", "Bo cau hoi da het han hoac da nop. Tai lai trang.");
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            if (bayGio < p.AnswerableAt)
            {
                throw new GateException(
                    409,
                    "chua_het_dem_nguoc",
                    "Con " + Math.Ceiling((p.AnswerableAt - bayGio).TotalSeconds) + " giay nua moi nop duoc.");
            }

            LinkCong l = LayLinkPhucVu(p.Code);
            DuAnPhucVu? d = p.ProjectId.HasValue ? _catalog.LayDuAn(p.ProjectId.Value) : null;

            // Kiem dinh dang TRUOC khi xoa phien: nhan sai dinh dang thi sua va nop lai duoc.
            Dictionary<Guid, LabelPayload> traLoi = d == null ? new Dictionary<Guid, LabelPayload>() : DocTraLoi(p, d, body);

            if (!await _redis.XoaPhienAsync(p.SessionId))
            {
                throw new GateException(409, "da_nop", "Bo cau hoi nay vua duoc nop.");
            }

            NguongKhop nguong = NguongKhopTuSetting.Doc(_settings);
            if (d != null && !LuatCongLink.DatCauVang(p.GoldIds, traLoi, d.DapAnVang, d.TapNhan, nguong))
            {
                await GhiNopAsync(p, l, KetQuaLuot.TruotCauVang, 0, null, null, bayGio);
                PhienVuot moi = await PhatPhienAsync(l, khach);
                return new SubmitResponse { Passed = false, NewSession = await TaoResponseAsync(moi) };
            }

            bool coCauHoi = d != null && p.RealIds.Count > 0;
            bool laChu = khach.UserId.HasValue && khach.UserId.Value == l.OwnerId;
            bool cungIp = l.CreatorIpHash != null && string.Equals(l.CreatorIpHash, p.IpHash, StringComparison.Ordinal);
            bool trungIp = false;
            bool conTien = false;
            TienLuot? tien = null;

            if (coCauHoi && !laChu && !cungIp)
            {
                // Chi "giu cho" IP khi khong phai tu vuot: tu vuot khong duoc an mat luot cua khach that.
                trungIp = !await _redis.GiuLuotIpAsync(l.LinkId, p.IpHashDay, _settings.ThoiGian(SettingKeys.GateIpDedupeWindow));
                tien = LuatCongLink.TinhTien(p.RealIds.Count, d!.DuAn.UnitPriceVnd, d.DuAn.PlatformFeeVnd);
                conTien = _catalog.NganSachConLai(d.DuAn.ProjectId) >= tien.TongVnd;
            }

            KetQuaLuot kq = LuatCongLink.PhanLoai(coCauHoi, laChu, cungIp, trungIp, conTien);

            GateSolved? nhan = null;
            if (coCauHoi)
            {
                nhan = TaoNhan(p, d!, traLoi, bayGio);
                await _redis.TangNhanMauAsync(p.RealIds);
            }

            ClickValidated? click = null;
            if (kq == KetQuaLuot.TinhTien)
            {
                click = new ClickValidated
                {
                    ClickId = Guid.CreateVersion7(),
                    LinkId = l.LinkId,
                    SharerId = l.OwnerId,
                    ProjectId = d!.DuAn.ProjectId,
                    LabelCount = p.RealIds.Count,
                    SharerAmountVnd = tien!.SharerVnd,
                    PlatformAmountVnd = tien.NenTangVnd,
                    ValidatedAt = bayGio,
                };
                _catalog.TruNganSach(d.DuAn.ProjectId, tien.TongVnd);
            }

            await GhiNopAsync(p, l, kq, coCauHoi ? p.RealIds.Count : 0, nhan, click, bayGio);

            string jti = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
            TimeSpan ttl = _settings.ThoiGian(SettingKeys.GateTokenTtl);
            DateTimeOffset hetHan = bayGio + ttl;
            await _redis.GhiJtiAsync(jti, ttl);
            string token = _token.Tao(jti, l.LinkId, hetHan);

            return new SubmitResponse
            {
                Passed = true,
                RedirectUrl = "/go/" + Uri.EscapeDataString(l.Code) + "?t=" + Uri.EscapeDataString(token),
                RedirectExpiresAt = hetHan,
            };
        }

        private static Dictionary<Guid, LabelPayload> DocTraLoi(PhienVuot p, DuAnPhucVu d, SubmitRequest? body)
        {
            Dictionary<Guid, LabelPayload> kq = new Dictionary<Guid, LabelPayload>();
            Dictionary<Guid, JsonElement> answers = body == null || body.Answers == null ? new Dictionary<Guid, JsonElement>() : body.Answers;

            foreach (Guid id in p.GoldIds.Concat(p.RealIds))
            {
                JsonElement e;
                if (!answers.TryGetValue(id, out e))
                {
                    throw new GateException(400, "thieu_cau_tra_loi", "Chua tra loi het cac cau hoi.");
                }

                // Kiem bang dung cua cua Crowd.Labeling nhu labeler chuyen nghiep: nhan sai → 400.
                kq[id] = LabelPayload.Tao(d.TapNhan, e, null);
            }

            return kq;
        }

        private GateSolved TaoNhan(PhienVuot p, DuAnPhucVu d, Dictionary<Guid, LabelPayload> traLoi, DateTimeOffset luc)
        {
            List<GateLabel> nhan = new List<GateLabel>();
            foreach (Guid id in p.RealIds)
            {
                MauCong? m = _catalog.LayMau(id);
                if (m == null)
                {
                    continue;
                }

                nhan.Add(new GateLabel
                {
                    SampleId = id,
                    StorageKey = m.StorageKey,
                    SampleContent = m.Content,
                    SampleMetadata = m.Metadata,
                    LabelPayload = traLoi[id],
                });
            }

            return new GateSolved { SessionId = p.SessionId, LinkId = p.LinkId, ProjectId = d.DuAn.ProjectId, Labels = nhan, SolvedAt = luc };
        }

        private async Task GhiNopAsync(PhienVuot p, LinkCong l, KetQuaLuot kq, int soNhan, GateSolved? nhan, ClickValidated? click, DateTimeOffset luc)
        {
            await GhiAsync(new MucSuKien
            {
                Row = new DongClick
                {
                    ClickId = click != null ? click.ClickId : Guid.CreateVersion7(),
                    At = luc,
                    Kind = "submit",
                    Outcome = JsonNamingPolicy.CamelCase.ConvertName(kq.ToString()),
                    LinkId = l.LinkId,
                    OwnerId = l.OwnerId,
                    CampaignId = l.CampaignId,
                    ProjectId = p.ProjectId,
                    LabelCount = soNhan,
                    RevenueVnd = click != null ? click.SharerAmountVnd : 0,
                    IpHashDay = p.IpHashDay,
                    ReferrerHost = p.ReferrerHost,
                },
                Solved = nhan,
                Click = click,
            });
        }

        private async Task GhiAsync(MucSuKien m)
        {
            await _redis.GhiSuKienAsync(JsonSerializer.Serialize(m, CrowdJson.Options));
        }

        // =====================================================================
        // BUOC 4: MO LINK DICH
        // =====================================================================

        /// <summary>Token hop le + chua dung → link dich. Token dung roi / het han / sai link → loi.</summary>
        public async Task<string> MoLinkAsync(string code, string? token)
        {
            LinkCong l = LayLinkPhucVu(code);

            string? jti = await _token.KiemAsync(token, l.LinkId);
            if (jti == null)
            {
                throw new GateException(403, "token_khong_hop_le", "Token khong hop le hoac da het han. Vuot link lai.");
            }

            if (!await _redis.DungJtiAsync(jti))
            {
                throw new GateException(410, "token_da_dung", "Token da duoc dung. Vuot link lai.");
            }

            return l.DestinationUrl;
        }

        private LinkCong LayLinkPhucVu(string code)
        {
            LinkCong? l = string.IsNullOrEmpty(code) || code.Length > 32 ? null : _catalog.LayLink(code);
            if (l == null || !l.PhucVuDuoc(_clock.GetUtcNow()))
            {
                // Chua quet xong, da vo hieu hoa, het han hay khong ton tai: cung mot cau tra loi.
                throw new GateException(404, "link_khong_ton_tai", "Link khong ton tai, da het han hoac da bi vo hieu hoa.");
            }

            return l;
        }
    }
}
