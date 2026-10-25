using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Persistence;
using Crowd.BuildingBlocks.Settings;
using Crowd.BuildingBlocks.Storage;
using Crowd.Identity.Api.Dtos;
using Crowd.Identity.Api.Entities;
using Crowd.Identity.Api.Exceptions;
using Crowd.Identity.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Identity.Api.Services
{
    /// <summary>
    /// Ho so ca nhan (FC-05), ky nang (FL-01), anh dai dien, ho so cong khai (ten + anh + cap do
    /// + huy hieu de man hinh duyet nhan / bang xep hang hien nguoi gan thay vi chi labelerId).
    /// </summary>
    public sealed class HoSoService
    {
        public const int SoHoSoCongKhaiToiDa = 100;

        private static readonly HashSet<string> DuoiAnh = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp" };

        private readonly IdentityDbContext _db;
        private readonly IObjectStorage _kho;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;

        public HoSoService(IdentityDbContext db, KhoAnhDaiDien kho, ISettings settings, TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (kho == null)
            {
                throw new ArgumentNullException(nameof(kho));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _db = db;
            _kho = kho.Kho;
            _settings = settings;
            _clock = clock;
        }

        public async Task<ProfileResponse> CuaToiAsync(Guid userId, CancellationToken ct)
        {
            return await TaoResponseAsync(await LayAsync(userId, ct));
        }

        public async Task<ProfileResponse> CapNhatAsync(Guid userId, UpdateProfileRequest body, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            User u = await LayAsync(userId, ct);
            u.CapNhatHoSo(body.DisplayName ?? string.Empty, body.Bio);
            if (body.Username != null && User.ChuanHoaUsername(body.Username) != u.Username)
            {
                string moi = User.ChuanHoaUsername(body.Username);
                u.DoiUsername(moi);
                if (await _db.Users.AnyAsync(x => x.Username == moi && x.Id != userId, ct))
                {
                    throw new RuleViolationException("username_da_ton_tai", "Username “" + moi + "” đã có người dùng — chọn tên khác.");
                }
            }

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex, "ux_users_username"))
            {
                throw new RuleViolationException("username_da_ton_tai", "Username vừa có người khác chọn — chọn tên khác.");
            }

            return await TaoResponseAsync(u);
        }

        public async Task<ProfileResponse> CapNhatKyNangAsync(Guid userId, SkillsDto body, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            User u = await LayAsync(userId, ct);
            u.CapNhatKyNang(
                body.Domains ?? Array.Empty<string>(),
                body.Languages ?? Array.Empty<string>(),
                body.Modalities ?? Array.Empty<string>());
            await _db.SaveChangesAsync(ct);
            return await TaoResponseAsync(u);
        }

        /// <summary>Xin link PUT anh dai dien. Key nam duoi thu muc cua chinh nguoi dung.</summary>
        public async Task<AvatarUploadResponse> XinLinkAvatarAsync(Guid userId, AvatarUploadRequest body)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            string ten = Path.GetFileName((body.FileName ?? string.Empty).Trim());
            string duoi = Path.GetExtension(ten);
            if (ten.Length == 0 || ten.Length > 200 || !DuoiAnh.Contains(duoi))
            {
                throw new InvalidValueException("file_khong_hop_le", "Ảnh đại diện phải là .png, .jpg hoặc .webp.");
            }

            long toiDa = _settings.SoLon(SettingKeys.IdentityAvatarMaxBytes);
            if (body.SizeBytes <= 0 || body.SizeBytes > toiDa)
            {
                throw new InvalidValueException("file_qua_lon", "Ảnh đại diện tối đa " + toiDa + " byte.");
            }

            string key = userId + "/" + Guid.CreateVersion7() + duoi.ToLowerInvariant();
            return new AvatarUploadResponse
            {
                Key = key,
                UploadUrl = await _kho.TaoLinkTaiLenAsync(key),
                Method = "PUT",
                ExpiresAt = _clock.GetUtcNow() + _settings.ThoiGian(SettingKeys.UploadLinkTtl),
            };
        }

        /// <summary>
        /// Gan anh vua tai len (key phai thuoc thu muc cua chinh minh va file THAT SU co trong kho,
        /// dung kich thuoc). key null → go anh.
        /// </summary>
        public async Task<ProfileResponse> DatAvatarAsync(Guid userId, string? key, CancellationToken ct)
        {
            User u = await LayAsync(userId, ct);

            if (key != null)
            {
                if (!key.StartsWith(userId + "/", StringComparison.Ordinal) || key.Length > 300 || key.Contains("..", StringComparison.Ordinal))
                {
                    throw new InvalidValueException("key_khong_hop_le", "Ảnh đại diện không thuộc tài khoản này.");
                }

                ThongTinFile? file = await _kho.ThongTinAsync(key, ct);
                if (file == null)
                {
                    throw new RuleViolationException("chua_tai_anh", "Chưa tải ảnh lên (link đã cấp nhưng chưa có file).");
                }

                if (file.SizeBytes > _settings.SoLon(SettingKeys.IdentityAvatarMaxBytes))
                {
                    throw new InvalidValueException("file_qua_lon", "Ảnh đại diện vượt kích thước cho phép.");
                }
            }

            u.DatAvatar(key);
            await _db.SaveChangesAsync(ct);
            return await TaoResponseAsync(u);
        }

        /// <summary>Ho so cong khai cua nhieu nguoi mot luc (man hinh duyet nhan, bang xep hang). Id la thi bo qua.</summary>
        public async Task<IReadOnlyList<PublicProfileResponse>> CongKhaiAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
        {
            if (ids == null)
            {
                throw new ArgumentNullException(nameof(ids));
            }

            List<Guid> ds = ids.Distinct().ToList();
            if (ds.Count == 0)
            {
                return new List<PublicProfileResponse>();
            }

            if (ds.Count > SoHoSoCongKhaiToiDa)
            {
                throw new InvalidValueException("qua_nhieu_id", "Tối đa " + SoHoSoCongKhaiToiDa + " id mỗi lần.");
            }

            List<User> users = await _db.Users.AsNoTracking().Where(u => ds.Contains(u.Id)).ToListAsync(ct);
            Dictionary<Guid, int> cap = await _db.ThongKeLabelers.AsNoTracking()
                .Where(t => ds.Contains(t.UserId)).ToDictionaryAsync(t => t.UserId, t => t.Level, ct);
            List<HuyHieuDaDat> huyHieu = await _db.HuyHieuDaDats.AsNoTracking()
                .Where(h => ds.Contains(h.UserId)).ToListAsync(ct);

            List<PublicProfileResponse> kq = new List<PublicProfileResponse>();
            foreach (User u in users)
            {
                int capDo;
                kq.Add(new PublicProfileResponse
                {
                    UserId = u.Id,
                    Username = u.Username,
                    DisplayName = u.DisplayName,
                    AvatarUrl = await LinkAvatarAsync(u.AvatarKey),
                    Level = cap.TryGetValue(u.Id, out capDo) ? capDo : 1,
                    Badges = TaoHuyHieu(huyHieu.Where(h => h.UserId == u.Id)),
                });
            }

            return kq;
        }

        public async Task<string?> LinkAvatarAsync(string? key)
        {
            if (key == null)
            {
                return null;
            }

            return await _kho.TaoLinkXemAsync(key);
        }

        public static List<BadgeDto> TaoHuyHieu(IEnumerable<HuyHieuDaDat> ds)
        {
            List<BadgeDto> kq = new List<BadgeDto>();
            foreach (HuyHieuDaDat h in ds.OrderByDescending(x => x.AwardedAt))
            {
                HuyHieu? dinhNghia = CapDo.Tim(h.Badge);
                kq.Add(new BadgeDto { Code = h.Badge, Name = dinhNghia == null ? h.Badge : dinhNghia.Ten, AwardedAt = h.AwardedAt });
            }

            return kq;
        }

        private async Task<ProfileResponse> TaoResponseAsync(User u)
        {
            return new ProfileResponse
            {
                UserId = u.Id,
                Email = u.Email,
                Username = u.Username,
                EmailVerified = u.DaXacThucEmail,
                DisplayName = u.DisplayName,
                Bio = u.Bio,
                AvatarUrl = await LinkAvatarAsync(u.AvatarKey),
                Roles = u.Roles,
                Skills = new SkillsDto { Domains = u.Domains, Languages = u.Languages, Modalities = u.Modalities },
                CreatedAt = u.CreatedAt,
            };
        }

        private async Task<User> LayAsync(Guid userId, CancellationToken ct)
        {
            User? u = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId, ct);
            if (u == null)
            {
                throw new NotFoundException("Không tìm thấy tài khoản.");
            }

            return u;
        }
    }
}
