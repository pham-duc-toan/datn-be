using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.BuildingBlocks.Settings;
using Crowd.Contracts.Identity;
using Crowd.Identity.Api.Dtos;
using Crowd.Identity.Api.Entities;
using Crowd.Identity.Api.Exceptions;
using Crowd.Identity.Api.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Identity.Api.Services
{
    /// <summary>
    /// Cai dat / bat / tat xac thuc hai lop (FC-04, tuy chon):
    ///   1. POST /me/2fa/setup   → bi mat moi + link otpauth (frontend ve QR). CHUA bat.
    ///   2. POST /me/2fa/enable  { code } → ma dung = nguoi dung da quet QR thanh cong → BAT,
    ///      tra 10 ma du phong (CHI hien MOT lan).
    ///   3. POST /me/2fa/disable { password, code } → tat (can ca hai: mat token dang nhap khong du).
    /// Bat / tat deu phat user.two_factor_changed → notification gui email canh bao.
    /// Buoc dang nhap thu hai nam o AuthService.XacThucHaiLopAsync.
    /// </summary>
    public sealed class HaiLopService
    {
        public const int SoMaDuPhong = 10;

        private const string BangChuMaDuPhong = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        private readonly IdentityDbContext _db;
        private readonly IOutboxWriter _outbox;
        private readonly KhoaDuLieu _khoa;
        private readonly MatKhauService _matKhau;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;

        public HaiLopService(IdentityDbContext db, IOutboxWriter outbox, KhoaDuLieu khoa, MatKhauService matKhau, ISettings settings, TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (outbox == null)
            {
                throw new ArgumentNullException(nameof(outbox));
            }

            if (khoa == null)
            {
                throw new ArgumentNullException(nameof(khoa));
            }

            if (matKhau == null)
            {
                throw new ArgumentNullException(nameof(matKhau));
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
            _outbox = outbox;
            _khoa = khoa;
            _matKhau = matKhau;
            _settings = settings;
            _clock = clock;
        }

        public async Task<TwoFactorStatusResponse> TrangThaiAsync(Guid userId, CancellationToken ct)
        {
            HaiLop? h = await _db.HaiLops.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
            return new TwoFactorStatusResponse
            {
                Enabled = h != null && h.DangBat,
                EnabledAt = h == null ? null : h.EnabledAt,
                RecoveryCodesLeft = h == null || !h.DangBat ? 0 : h.RecoveryHashes.Count,
            };
        }

        /// <summary>Sinh bi mat moi (hoac thay bi mat cua lan cai dat chua xong). Dang bat → 409.</summary>
        public async Task<TwoFactorSetupResponse> CaiDatAsync(Guid userId, CancellationToken ct)
        {
            User user = await LayUserAsync(userId, ct);
            HaiLop? h = await _db.HaiLops.FirstOrDefaultAsync(x => x.UserId == userId, ct);
            if (h != null && h.DangBat)
            {
                throw new RuleViolationException("hai_lop_dang_bat", "2FA dang bat — tat truoc khi cai dat lai.");
            }

            byte[] biMat = Totp.TaoBiMat();
            string maHoa = _khoa.MaHoa(biMat);
            DateTimeOffset bayGio = _clock.GetUtcNow();
            if (h == null)
            {
                _db.HaiLops.Add(HaiLop.BatDauCaiDat(userId, maHoa, bayGio));
            }
            else
            {
                h.DoiBiMat(maHoa, bayGio);
            }

            await _db.SaveChangesAsync(ct);

            string nhaPhatHanh = _settings.Chuoi(SettingKeys.IdentityTwoFactorIssuer);
            return new TwoFactorSetupResponse
            {
                Secret = Base32.Ma(biMat),
                OtpauthUri = Totp.TaoLinkOtpAuth(nhaPhatHanh, user.Email, biMat),
            };
        }

        /// <summary>Nhap ma tu ung dung de xac nhan → bat 2FA, tra ma du phong.</summary>
        public async Task<TwoFactorRecoveryCodesResponse> BatAsync(string? ma, Caller caller, CancellationToken ct)
        {
            Guid userId = caller.LayUserId();
            HaiLop? h = await _db.HaiLops.FirstOrDefaultAsync(x => x.UserId == userId, ct);
            if (h == null)
            {
                throw new RuleViolationException("chua_cai_dat", "Goi POST /me/2fa/setup truoc.");
            }

            if (h.DangBat)
            {
                throw new RuleViolationException("hai_lop_dang_bat", "2FA da bat.");
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            long buoc = Totp.Kiem(_khoa.GiaiMa(h.SecretCipher), ma, bayGio, h.LastUsedStep);
            if (buoc < 0)
            {
                throw new InvalidValueException("ma_2fa_sai", "Ma khong dung — kiem tra dong ho dien thoai va quet lai ma QR.");
            }

            List<string> maGoc = TaoMaDuPhong();
            h.Bat(buoc, BamTatCa(maGoc), bayGio);
            Phat(caller, new UserTwoFactorChanged { UserId = userId, Enabled = true, Reason = "enabled" });

            await _db.SaveChangesAsync(ct);
            return new TwoFactorRecoveryCodesResponse { RecoveryCodes = maGoc };
        }

        /// <summary>Tat 2FA: can mat khau VA ma 6 so (hoac ma du phong).</summary>
        public async Task TatAsync(string? matKhau, string? ma, string? maDuPhong, Caller caller, CancellationToken ct)
        {
            Guid userId = caller.LayUserId();
            User user = await LayUserAsync(userId, ct);
            HaiLop h = await LayDangBatAsync(userId, ct);

            KiemMatKhau(user, matKhau);
            KiemMaHoacDuPhong(h, ma, maDuPhong);

            _db.HaiLops.Remove(h);
            Phat(caller, new UserTwoFactorChanged { UserId = userId, Enabled = false, Reason = "disabled" });
            await _db.SaveChangesAsync(ct);
        }

        /// <summary>Sinh lai bo ma du phong (bo cu het hieu luc). Can ma 6 so.</summary>
        public async Task<TwoFactorRecoveryCodesResponse> TaoLaiMaDuPhongAsync(string? ma, Caller caller, CancellationToken ct)
        {
            Guid userId = caller.LayUserId();
            HaiLop h = await LayDangBatAsync(userId, ct);
            KiemMaHoacDuPhong(h, ma, null);

            List<string> maGoc = TaoMaDuPhong();
            h.DatMaDuPhong(BamTatCa(maGoc));
            await _db.SaveChangesAsync(ct);
            return new TwoFactorRecoveryCodesResponse { RecoveryCodes = maGoc };
        }

        /// <summary>
        /// Admin go 2FA cho nguoi mat ca dien thoai lan ma du phong (sau khi xac minh chu tai khoan
        /// ngoai he thong). Phat email canh bao nhu khi tu tat.
        /// </summary>
        public async Task AdminGoAsync(Guid userId, Caller caller, CancellationToken ct)
        {
            await LayUserAsync(userId, ct);
            HaiLop? h = await _db.HaiLops.FirstOrDefaultAsync(x => x.UserId == userId, ct);
            if (h == null || !h.DangBat)
            {
                throw new RuleViolationException("hai_lop_chua_bat", "Tai khoan nay chua bat 2FA.");
            }

            _db.HaiLops.Remove(h);
            Phat(caller, new UserTwoFactorChanged { UserId = userId, Enabled = false, Reason = "admin_reset" });
            await _db.SaveChangesAsync(ct);
        }

        // =====================================================================
        // Ma du phong — dung chung voi AuthService
        // =====================================================================

        /// <summary>Bo khoang trang / gach, chu hoa; khong dung 10 ky tu trong bang chu thi null.</summary>
        public static string? ChuanHoaMaDuPhong(string? ma)
        {
            if (ma == null)
            {
                return null;
            }

            StringBuilder sb = new StringBuilder();
            foreach (char c in ma)
            {
                if (c == ' ' || c == '-')
                {
                    continue;
                }

                char hoa = char.ToUpperInvariant(c);
                if (BangChuMaDuPhong.IndexOf(hoa) < 0)
                {
                    return null;
                }

                sb.Append(hoa);
            }

            return sb.Length == 10 ? sb.ToString() : null;
        }

        /// <summary>Bam CO KHOA: 10 ky tu ~ 50 bit, bam tran thi do nguoc duoc khi lo database.</summary>
        public static string BamMaDuPhong(KhoaDuLieu khoa, string maDaChuanHoa)
        {
            if (khoa == null)
            {
                throw new ArgumentNullException(nameof(khoa));
            }

            return khoa.BamCoKhoa("recovery:" + maDaChuanHoa);
        }

        private static List<string> TaoMaDuPhong()
        {
            List<string> ds = new List<string>();
            for (int i = 0; i < SoMaDuPhong; i++)
            {
                StringBuilder sb = new StringBuilder();
                for (int j = 0; j < 10; j++)
                {
                    if (j == 5)
                    {
                        sb.Append('-');
                    }

                    sb.Append(BangChuMaDuPhong[RandomNumberGenerator.GetInt32(BangChuMaDuPhong.Length)]);
                }

                ds.Add(sb.ToString());
            }

            return ds;
        }

        private List<string> BamTatCa(List<string> maGoc)
        {
            List<string> ds = new List<string>();
            foreach (string m in maGoc)
            {
                ds.Add(BamMaDuPhong(_khoa, ChuanHoaMaDuPhong(m)!));
            }

            return ds;
        }

        private void KiemMatKhau(User user, string? matKhau)
        {
            if (string.IsNullOrEmpty(matKhau)
                || matKhau.Length > _settings.SoNguyen(SettingKeys.IdentityPasswordMaxLength)
                || _matKhau.Kiem(user, matKhau) == PasswordVerificationResult.Failed)
            {
                throw new InvalidValueException("mat_khau_hien_tai_sai", "Mat khau khong dung.");
            }
        }

        private void KiemMaHoacDuPhong(HaiLop h, string? ma, string? maDuPhong)
        {
            if (!string.IsNullOrWhiteSpace(maDuPhong))
            {
                string? sach = ChuanHoaMaDuPhong(maDuPhong);
                if (sach == null || !h.DungMaDuPhong(BamMaDuPhong(_khoa, sach)))
                {
                    throw new InvalidValueException("ma_2fa_sai", "Ma du phong khong dung hoac da dung.");
                }

                return;
            }

            long buoc = Totp.Kiem(_khoa.GiaiMa(h.SecretCipher), ma, _clock.GetUtcNow(), h.LastUsedStep);
            if (buoc < 0)
            {
                throw new InvalidValueException("ma_2fa_sai", "Ma 2FA khong dung.");
            }

            h.GhiBuocDaDung(buoc);
        }

        private async Task<HaiLop> LayDangBatAsync(Guid userId, CancellationToken ct)
        {
            HaiLop? h = await _db.HaiLops.FirstOrDefaultAsync(x => x.UserId == userId, ct);
            if (h == null || !h.DangBat)
            {
                throw new RuleViolationException("hai_lop_chua_bat", "2FA chua bat.");
            }

            return h;
        }

        private async Task<User> LayUserAsync(Guid userId, CancellationToken ct)
        {
            User? u = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId, ct);
            if (u == null)
            {
                throw new NotFoundException("Khong tim thay tai khoan.");
            }

            return u;
        }

        private void Phat<TPayload>(Caller caller, TPayload payload)
            where TPayload : class, IEventPayload
        {
            _outbox.Enqueue(EventEnvelope.Create(
                producer: "identity-svc",
                correlationId: caller.CorrelationId,
                payload: payload,
                causationId: caller.CausationId,
                actor: caller.TaoActor()));
        }
    }
}
