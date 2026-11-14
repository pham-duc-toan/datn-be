using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Crowd.BuildingBlocks.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Crowd.BuildingBlocks.Storage
{
    /// <summary>Thong so link xem mau qua media-svc. Doc tu muc "MediaLinks".</summary>
    public sealed class MediaLinksOptions
    {
        public const string SectionName = "MediaLinks";

        /// <summary>Dia chi gateway trinh duyet toi duoc: link xem = {PublicUrl}/media/v/{token}.</summary>
        public string PublicUrl { get; set; } = "http://localhost:8080";

        /// <summary>
        /// Khoa AES-256 (base64, 32 byte) dung CHUNG voi media-svc (bien MEDIA_GRANT_KEY). Service
        /// tu ky link, khong goi HTTP sang media-svc; ai khong co khoa thi khong tao / doc / sua duoc link.
        /// </summary>
        public string GrantKey { get; set; } = string.Empty;
    }

    /// <summary>Nguoi xem nhan ban nao cua mau (FP-02 / FP-03).</summary>
    public enum CheDoXem
    {
        /// <summary>Ban goc — chi chu du an va admin.</summary>
        Goc,

        /// <summary>
        /// Ban co watermark — labeler, reviewer, khach cong link. Anh / video: chu NOI mo goc duoi phai
        /// "nguon: &lt;nguoi xem&gt; - &lt;doanh nghiep&gt; - &lt;domain&gt;"; am thanh: watermark CHIM mang ma nguoi xem.
        /// </summary>
        Watermark,
    }

    /// <summary>Mau can phat link xem. StorageKey nam TRONG token da ma hoa — client khong bao gio thay.</summary>
    public sealed class MauCanXem
    {
        public required Guid SampleId { get; init; }

        public required Guid ProjectId { get; init; }

        public required string StorageKey { get; init; }

        public required string Modality { get; init; }

        /// <summary>Ten doanh nghiep chu du an (identity) — in vao watermark noi. null = khong biet (bo khoi chu).</summary>
        public string? TenDoanhNghiep { get; init; }

        /// <summary>Video cat doan: giay bat dau CUA MAU trong file (metadata segmentStart, khong cat = 0).</summary>
        public double? BatDau { get; init; }

        /// <summary>Giay ket thuc CUA MAU trong file (metadata segmentEnd, khong cat = durationSec).</summary>
        public double? KetThuc { get; init; }
    }

    /// <summary>
    /// Noi dung watermark noi (anh / video) — MOT cho dinh dang duy nhat. project, gate dung chu nay khi tao link
    /// (dat trong token) va tra cho frontend ve de len trinh phat; media-svc chi ve chu nhan duoc, khong tu ghep.
    /// </summary>
    public static class ChuWatermark
    {
        /// <summary>"nguồn: &lt;userId | ma phien khach&gt; - &lt;doanh nghiep&gt; - &lt;domain&gt;".</summary>
        public static string CaNhan(NguoiXem nguoi, string? tenDoanhNghiep, string domain)
        {
            if (nguoi == null)
            {
                throw new ArgumentNullException(nameof(nguoi));
            }

            Guid? ma = nguoi.UserId.HasValue ? nguoi.UserId : nguoi.GuestSessionId;
            return Ghep(ma.HasValue ? ma.Value.ToString() : null, tenDoanhNghiep, domain);
        }

        /// <summary>Phan DUNG CHUNG cua video (khong co nguoi xem): "nguồn: &lt;doanh nghiep&gt; - &lt;domain&gt;".</summary>
        public static string DungChung(string? tenDoanhNghiep, string domain)
        {
            return Ghep(null, tenDoanhNghiep, domain);
        }

        /// <summary>Domain in vao chu = host[:port] cua dia chi trang web (setting identity.frontend_url).</summary>
        public static string DomainTu(string? diaChiTrang)
        {
            Uri? uri;
            if (!string.IsNullOrWhiteSpace(diaChiTrang) && Uri.TryCreate(diaChiTrang.Trim(), UriKind.Absolute, out uri))
            {
                return uri.IsDefaultPort ? uri.Host : uri.Authority;
            }

            return (diaChiTrang ?? string.Empty).Trim().TrimEnd('/');
        }

        private static string Ghep(string? nguoiXem, string? tenDoanhNghiep, string domain)
        {
            StringBuilder sb = new StringBuilder("nguồn:");
            string tach = " ";
            foreach (string? phan in new string?[] { nguoiXem, tenDoanhNghiep, domain })
            {
                if (!string.IsNullOrWhiteSpace(phan))
                {
                    sb.Append(tach).Append(phan.Trim());
                    tach = " - ";
                }
            }

            return sb.ToString();
        }
    }

    /// <summary>Ai xem — ma cua nguoi nay duoc nhung vao watermark (FP-02).</summary>
    public sealed class NguoiXem
    {
        private NguoiXem(Guid? userId, Guid? guestSessionId, string role)
        {
            UserId = userId;
            GuestSessionId = guestSessionId;
            Role = role;
        }

        public Guid? UserId { get; }

        /// <summary>Phien cong link cua khach vang lai (khong dang nhap).</summary>
        public Guid? GuestSessionId { get; }

        /// <summary>labeler | reviewer | owner | admin | guest.</summary>
        public string Role { get; }

        public static NguoiXem Labeler(Guid userId)
        {
            return new NguoiXem(userId, null, "labeler");
        }

        public static NguoiXem Reviewer(Guid userId)
        {
            return new NguoiXem(userId, null, "reviewer");
        }

        public static NguoiXem ChuDuAn(Guid userId)
        {
            return new NguoiXem(userId, null, "owner");
        }

        public static NguoiXem Admin(Guid userId)
        {
            return new NguoiXem(userId, null, "admin");
        }

        public static NguoiXem Khach(Guid guestSessionId)
        {
            return new NguoiXem(null, guestSessionId, "guest");
        }
    }

    /// <summary>Tao link xem mau qua media-svc (thay link MinIO ky san — FP-03).</summary>
    public interface IMediaLinks
    {
        /// <summary>
        /// Link xem. Video co watermark → danh sach phat HLS "{PublicUrl}/media/v/{token}/index.m3u8" (chi doan cua mau,
        /// moc 0 = dau mau); con lai → "{PublicUrl}/media/v/{token}" (file nguyen, tua bang Range).
        /// </summary>
        string TaoLink(MauCanXem mau, NguoiXem nguoi, CheDoXem cheDo);

        /// <summary>
        /// Chu watermark noi frontend ve de len trinh phat VIDEO (phan giua video dung chung chi mang doanh nghiep +
        /// domain; lop nay mang ca nguoi xem — quay man hinh van thay). null = khong ve (ban goc, khong phai video, tat watermark).
        /// </summary>
        string? ChuDeLenTrinhPhat(MauCanXem mau, NguoiXem nguoi, CheDoXem cheDo);

        /// <summary>Chu in vao phan giua video luc nap (dung chung moi nguoi xem): "nguồn: &lt;doanh nghiep&gt; - &lt;domain&gt;".</summary>
        string ChuDungChung(string? tenDoanhNghiep);
    }

    /// <summary>
    /// Link = {PublicUrl}/media/v/{token}. Token = AES-256-GCM(JSON quyen xem): media-svc giai ma, kiem
    /// han, roi phat dung ban (goc / co watermark). Ma hoa chu khong chi ky vi trong token co khoa
    /// MinIO — client khong duoc biet. Song bang storage.view_link_ttl (mac dinh 5 phut).
    /// </summary>
    public sealed class MediaLinks : IMediaLinks
    {
        private readonly MediaLinksOptions _options;
        private readonly byte[] _khoa;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;

        public MediaLinks(IOptions<MediaLinksOptions> options, ISettings settings, TimeProvider clock)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _options = options.Value;
            _khoa = MediaGrant.DocKhoa(_options.GrantKey);
        }

        public string TaoLink(MauCanXem mau, NguoiXem nguoi, CheDoXem cheDo)
        {
            if (mau == null)
            {
                throw new ArgumentNullException(nameof(mau));
            }

            if (nguoi == null)
            {
                throw new ArgumentNullException(nameof(nguoi));
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            TimeSpan han = _settings.ThoiGian(SettingKeys.StorageViewLinkTtl);
            bool coWatermark = CoWatermark(mau, cheDo);
            string? chuCaNhan = null;
            string? chuChung = null;
            if (coWatermark && mau.Modality != "audio")
            {
                chuCaNhan = ChuWatermark.CaNhan(nguoi, mau.TenDoanhNghiep, Domain());
                chuChung = ChuDungChung(mau.TenDoanhNghiep);
            }

            byte[] noiDung = MediaGrant.TaoNoiDung(mau, nguoi, cheDo, Guid.NewGuid(), bayGio, bayGio + han, chuCaNhan, chuChung);
            string token = MediaGrant.MaHoa(_khoa, noiDung, RandomNumberGenerator.GetBytes(MediaGrant.DoDaiNonce));
            string link = _options.PublicUrl.TrimEnd('/') + "/media/v/" + token;
            if (coWatermark && mau.Modality == "video")
            {
                return link + "/" + MediaGrant.TenDanhSachPhat;
            }

            return link;
        }

        public string? ChuDeLenTrinhPhat(MauCanXem mau, NguoiXem nguoi, CheDoXem cheDo)
        {
            if (mau == null)
            {
                throw new ArgumentNullException(nameof(mau));
            }

            if (nguoi == null)
            {
                throw new ArgumentNullException(nameof(nguoi));
            }

            if (mau.Modality != "video" || !CoWatermark(mau, cheDo))
            {
                return null;
            }

            return ChuWatermark.CaNhan(nguoi, mau.TenDoanhNghiep, Domain());
        }

        public string ChuDungChung(string? tenDoanhNghiep)
        {
            return ChuWatermark.DungChung(tenDoanhNghiep, Domain());
        }

        /// <summary>
        /// Ban watermark khi: nguoi xem nhan ban watermark (khong phai chu / admin), loai co file (anh / am thanh / video)
        /// va admin chua tat (media.watermark_enabled). media-svc kiem lai setting voi anh / am thanh.
        /// </summary>
        private bool CoWatermark(MauCanXem mau, CheDoXem cheDo)
        {
            return cheDo == CheDoXem.Watermark
                && (mau.Modality == "image" || mau.Modality == "video" || mau.Modality == "audio")
                && _settings.DungSai(SettingKeys.MediaWatermarkEnabled);
        }

        private string Domain()
        {
            return ChuWatermark.DomainTu(_settings.Chuoi(SettingKeys.IdentityFrontendUrl));
        }
    }

    /// <summary>
    /// Dinh dang token dung chung voi media-svc (Python: app/grant.py). Doi o day thi doi ca hai ben —
    /// test MediaGrantTests giu mot vector co dinh ma test Python cung giai duoc.
    /// </summary>
    public static class MediaGrant
    {
        public const int DoDaiNonce = 12;
        public const int DoDaiTag = 16;
        public const int PhienBan = 1;

        /// <summary>Video co watermark: ten danh sach phat HLS sau token; cac doan "c/…", "p/…" tuong doi voi no.</summary>
        public const string TenDanhSachPhat = "index.m3u8";

        /// <summary>Du lieu xac thuc kem (AAD): token cua ung dung khac dung cung khoa cung khong lot qua.</summary>
        public static readonly byte[] Aad = Encoding.ASCII.GetBytes("crowd-media-grant-v1");

        public static byte[] DocKhoa(string base64)
        {
            byte[] khoa;
            try
            {
                khoa = Convert.FromBase64String(base64 ?? string.Empty);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException("MediaLinks:GrantKey phai la base64 cua 32 byte.", ex);
            }

            if (khoa.Length != 32)
            {
                throw new InvalidOperationException("MediaLinks:GrantKey phai la base64 cua dung 32 byte (AES-256).");
            }

            return khoa;
        }

        public static byte[] TaoNoiDung(MauCanXem mau, NguoiXem nguoi, CheDoXem cheDo, Guid grantId, DateTimeOffset luc, DateTimeOffset het)
        {
            return TaoNoiDung(mau, nguoi, cheDo, grantId, luc, het, null, null);
        }

        /// <summary>
        /// chuCaNhan ("wt"): chu noi ve len anh / doan dau + cuoi video. chuChung ("ws"): chu phan giua video — media-svc
        /// dung khi phai tao lai doan dung chung luc phat. "a" / "b": doan cua mau trong file video. Ca bon TUY CHON: khong
        /// co thi KHONG ghi (am thanh, ban goc, token kieu cu giu nguyen dinh dang).
        /// </summary>
        public static byte[] TaoNoiDung(
            MauCanXem mau, NguoiXem nguoi, CheDoXem cheDo, Guid grantId, DateTimeOffset luc, DateTimeOffset het, string? chuCaNhan, string? chuChung)
        {
            if (mau == null)
            {
                throw new ArgumentNullException(nameof(mau));
            }

            if (nguoi == null)
            {
                throw new ArgumentNullException(nameof(nguoi));
            }

            using (MemoryStream ms = new MemoryStream())
            {
                using (Utf8JsonWriter w = new Utf8JsonWriter(ms))
                {
                    w.WriteStartObject();
                    w.WriteNumber("v", PhienBan);
                    w.WriteString("g", grantId);
                    w.WriteString("s", mau.SampleId);
                    w.WriteString("p", mau.ProjectId);
                    w.WriteString("k", mau.StorageKey);
                    w.WriteString("mo", mau.Modality);
                    VietGuid(w, "u", nguoi.UserId);
                    VietGuid(w, "gs", nguoi.GuestSessionId);
                    w.WriteString("r", nguoi.Role);
                    w.WriteString("m", TenCheDo(cheDo));
                    w.WriteNumber("iat", luc.ToUnixTimeSeconds());
                    w.WriteNumber("exp", het.ToUnixTimeSeconds());
                    if (chuCaNhan != null)
                    {
                        w.WriteString("wt", chuCaNhan);
                    }

                    if (chuChung != null)
                    {
                        w.WriteString("ws", chuChung);
                    }

                    if (mau.BatDau.HasValue && mau.KetThuc.HasValue && mau.KetThuc.Value > mau.BatDau.Value)
                    {
                        w.WriteNumber("a", Math.Round(mau.BatDau.Value, 3));
                        w.WriteNumber("b", Math.Round(mau.KetThuc.Value, 3));
                    }

                    w.WriteEndObject();
                }

                return ms.ToArray();
            }
        }

        public static string TenCheDo(CheDoXem cheDo)
        {
            switch (cheDo)
            {
                case CheDoXem.Goc:
                    return "original";
                case CheDoXem.Watermark:
                    return "watermarked";
                default:
                    throw new ArgumentOutOfRangeException(nameof(cheDo));
            }
        }

        /// <summary>token = base64url(nonce 12 byte | ban ma | tag 16 byte).</summary>
        public static string MaHoa(byte[] khoa, byte[] noiDung, byte[] nonce)
        {
            if (khoa == null)
            {
                throw new ArgumentNullException(nameof(khoa));
            }

            if (noiDung == null)
            {
                throw new ArgumentNullException(nameof(noiDung));
            }

            if (nonce == null || nonce.Length != DoDaiNonce)
            {
                throw new ArgumentException("Nonce phai dai 12 byte.", nameof(nonce));
            }

            byte[] banMa = new byte[noiDung.Length];
            byte[] tag = new byte[DoDaiTag];
            using (AesGcm aes = new AesGcm(khoa, DoDaiTag))
            {
                aes.Encrypt(nonce, noiDung, banMa, tag, Aad);
            }

            byte[] tatCa = new byte[DoDaiNonce + banMa.Length + DoDaiTag];
            Buffer.BlockCopy(nonce, 0, tatCa, 0, DoDaiNonce);
            Buffer.BlockCopy(banMa, 0, tatCa, DoDaiNonce, banMa.Length);
            Buffer.BlockCopy(tag, 0, tatCa, DoDaiNonce + banMa.Length, DoDaiTag);
            return Base64Url(tatCa);
        }

        /// <summary>Giai ma — media-svc lam viec nay ben Python; o day de test dinh dang khu hoi.</summary>
        public static byte[] GiaiMa(byte[] khoa, string token)
        {
            byte[] tatCa = TuBase64Url(token);
            if (tatCa.Length < DoDaiNonce + DoDaiTag)
            {
                throw new CryptographicException("Token qua ngan.");
            }

            byte[] nonce = new byte[DoDaiNonce];
            byte[] banMa = new byte[tatCa.Length - DoDaiNonce - DoDaiTag];
            byte[] tag = new byte[DoDaiTag];
            Buffer.BlockCopy(tatCa, 0, nonce, 0, DoDaiNonce);
            Buffer.BlockCopy(tatCa, DoDaiNonce, banMa, 0, banMa.Length);
            Buffer.BlockCopy(tatCa, DoDaiNonce + banMa.Length, tag, 0, DoDaiTag);

            byte[] noiDung = new byte[banMa.Length];
            using (AesGcm aes = new AesGcm(khoa, DoDaiTag))
            {
                aes.Decrypt(nonce, banMa, tag, noiDung, Aad);
            }

            return noiDung;
        }

        private static void VietGuid(Utf8JsonWriter w, string ten, Guid? giaTri)
        {
            if (giaTri.HasValue)
            {
                w.WriteString(ten, giaTri.Value);
            }
            else
            {
                w.WriteNull(ten);
            }
        }

        private static string Base64Url(byte[] du)
        {
            return Convert.ToBase64String(du).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static byte[] TuBase64Url(string s)
        {
            string chuan = (s ?? string.Empty).Replace('-', '+').Replace('_', '/');
            switch (chuan.Length % 4)
            {
                case 2:
                    chuan = chuan + "==";
                    break;
                case 3:
                    chuan = chuan + "=";
                    break;
            }

            return Convert.FromBase64String(chuan);
        }
    }

    public static class MediaLinksExtensions
    {
        /// <summary>Dang ky IMediaLinks (can ISettings da dang ky). Thieu / sai MediaLinks:GrantKey thi loi ngay luc khoi dong.</summary>
        public static IServiceCollection AddMediaLinks(this IServiceCollection services, IConfiguration configuration)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            services.AddOptions<MediaLinksOptions>()
                .Bind(configuration.GetSection(MediaLinksOptions.SectionName))
                .Validate(o => KhoaHopLe(o.GrantKey), "MediaLinks:GrantKey phai la base64 cua dung 32 byte (AES-256).")
                .ValidateOnStart();
            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton<IMediaLinks, MediaLinks>();
            return services;
        }

        private static bool KhoaHopLe(string? base64)
        {
            try
            {
                MediaGrant.DocKhoa(base64 ?? string.Empty);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }
}
