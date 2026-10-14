using System;
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
    /// Xac thuc email (FC-01), quen / dat lai / doi mat khau (FC-03), dang xuat moi thiet bi.
    /// </summary>
    public sealed class TaiKhoanService
    {
        /// <summary>Hai lan xin gui lai email cach nhau it nhat bay nhieu (chong spam hop thu).</summary>
        public static readonly TimeSpan KhoangCachGuiLai = TimeSpan.FromSeconds(60);

        private readonly IdentityDbContext _db;
        private readonly IOutboxWriter _outbox;
        private readonly MaTaiKhoanService _ma;
        private readonly PhienService _phien;
        private readonly AuthService _auth;
        private readonly MatKhauService _matKhau;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;

        public TaiKhoanService(
            IdentityDbContext db,
            IOutboxWriter outbox,
            MaTaiKhoanService ma,
            PhienService phien,
            AuthService auth,
            MatKhauService matKhau,
            ISettings settings,
            TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (outbox == null)
            {
                throw new ArgumentNullException(nameof(outbox));
            }

            if (ma == null)
            {
                throw new ArgumentNullException(nameof(ma));
            }

            if (phien == null)
            {
                throw new ArgumentNullException(nameof(phien));
            }

            if (auth == null)
            {
                throw new ArgumentNullException(nameof(auth));
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
            _ma = ma;
            _phien = phien;
            _auth = auth;
            _matKhau = matKhau;
            _settings = settings;
            _clock = clock;
        }

        // =====================================================================
        // XAC THUC EMAIL
        // =====================================================================

        /// <summary>Nguoi dung bam link trong email. Ma sai / het han / da dung → 400 ma_khong_hop_le.</summary>
        public async Task<EmailVerifiedResponse> XacThucEmailAsync(string? maGoc, Caller caller, CancellationToken ct)
        {
            MaTaiKhoan ma = await LayMaAsync(maGoc, MucDichMa.VerifyEmail, ct);
            User user = await LayUserAsync(ma.UserId, ct);

            ma.Dung(_clock.GetUtcNow());
            DanhDauDaXacThuc(user, caller);

            await _db.SaveChangesAsync(ct);
            return new EmailVerifiedResponse { EmailVerified = true };
        }

        /// <summary>Gui lai link xac thuc. Da xac thuc → 409 da_xac_thuc; gui lien tuc → 409 gui_qua_nhanh.</summary>
        public async Task GuiLaiXacThucEmailAsync(Caller caller, CancellationToken ct)
        {
            User user = await LayUserAsync(caller.LayUserId(), ct);
            if (user.DaXacThucEmail)
            {
                throw new RuleViolationException("da_xac_thuc", "Email da duoc xac thuc.");
            }

            await DamBaoKhongGuiQuaNhanhAsync(user.Id, MucDichMa.VerifyEmail, true, ct);
            await _ma.GuiXacThucEmailAsync(user, caller, ct);
            await _db.SaveChangesAsync(ct);
        }

        // =====================================================================
        // QUEN / DAT LAI / DOI MAT KHAU
        // =====================================================================

        /// <summary>
        /// Luon "thanh cong" voi nguoi goi — email khong ton tai, bi khoa hay vua xin xong deu im lang,
        /// khong tiet lo email nao co tai khoan (VD-S-13).
        /// </summary>
        public async Task QuenMatKhauAsync(string? email, Caller caller, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(email) || email.Length > 320)
            {
                return;
            }

            string chuanHoa = User.ChuanHoaEmail(email);
            User? user = await _db.Users.FirstOrDefaultAsync(u => u.Email == chuanHoa, ct);
            if (user == null || user.Status == UserStatus.Blocked)
            {
                return;
            }

            if (!await DamBaoKhongGuiQuaNhanhAsync(user.Id, MucDichMa.ResetPassword, false, ct))
            {
                return;
            }

            await _ma.GuiDatLaiMatKhauAsync(user, caller, ct);
            await _db.SaveChangesAsync(ct);
        }

        /// <summary>
        /// Dat mat khau moi bang ma trong email. Moi phien cu bi thu hoi. Bam duoc link trong hop thu
        /// thi cung chung minh so huu email → xac thuc luon neu chua.
        /// </summary>
        public async Task DatLaiMatKhauAsync(string? maGoc, string? matKhauMoi, Caller caller, CancellationToken ct)
        {
            KiemMatKhauMoi(matKhauMoi);
            MaTaiKhoan ma = await LayMaAsync(maGoc, MucDichMa.ResetPassword, ct);
            User user = await LayUserAsync(ma.UserId, ct);
            if (user.Status == UserStatus.Blocked)
            {
                throw new RuleViolationException("tai_khoan_bi_khoa", "Tai khoan dang bi khoa.");
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                ma.Dung(bayGio);
                user.DatPasswordHash(_matKhau.Bam(user, matKhauMoi!));
                DanhDauDaXacThuc(user, caller);
                await _phien.ThuHoiTatCaAsync(user.Id, "password_reset", caller, bayGio, ct);

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            finally
            {
                await tx.DisposeAsync();
            }
        }

        /// <summary>
        /// Doi mat khau khi dang dang nhap. Moi phien cu (ke ca thiet bi khac) bi thu hoi; tra ve
        /// cap token MOI cho chinh thiet bi dang doi de khong bi dang xuat.
        /// </summary>
        public async Task<TokenResponse> DoiMatKhauAsync(string? matKhauHienTai, string? matKhauMoi, Caller caller, CancellationToken ct)
        {
            KiemMatKhauMoi(matKhauMoi);
            User user = await LayUserAsync(caller.LayUserId(), ct);

            if (string.IsNullOrEmpty(matKhauHienTai)
                || matKhauHienTai.Length > _settings.SoNguyen(SettingKeys.IdentityPasswordMaxLength)
                || _matKhau.Kiem(user, matKhauHienTai) == PasswordVerificationResult.Failed)
            {
                throw new InvalidValueException("mat_khau_hien_tai_sai", "Mat khau hien tai khong dung.");
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                user.DatPasswordHash(_matKhau.Bam(user, matKhauMoi!));
                await _phien.ThuHoiTatCaAsync(user.Id, "password_changed", caller, bayGio, ct);
                TokenResponse capMoi = await _auth.MoPhienAsync(user, ct);

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return capMoi;
            }
            finally
            {
                await tx.DisposeAsync();
            }
        }

        /// <summary>Dang xuat MOI thiet bi: thu hoi moi refresh token + access token dang song (qua gateway).</summary>
        public async Task DangXuatMoiThietBiAsync(Caller caller, CancellationToken ct)
        {
            Guid userId = caller.LayUserId();
            var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                await _phien.ThuHoiTatCaAsync(userId, "logout_all", caller, _clock.GetUtcNow(), ct);
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            finally
            {
                await tx.DisposeAsync();
            }
        }

        // =====================================================================
        // Ham dung chung
        // =====================================================================

        private void DanhDauDaXacThuc(User user, Caller caller)
        {
            if (!user.XacThucEmail(_clock.GetUtcNow()))
            {
                return;
            }

            _outbox.Enqueue(EventEnvelope.Create(
                producer: "identity-svc",
                correlationId: caller.CorrelationId,
                payload: new UserEmailVerified { UserId = user.Id },
                causationId: caller.CausationId,
                actor: caller.TaoActor()));
        }

        /// <summary>
        /// Lan phat gan nhat chua toi KhoangCachGuiLai → nemLoi: 409 gui_qua_nhanh; khong thi tra false
        /// (quen mat khau im lang, khong tiet lo).
        /// </summary>
        private async Task<bool> DamBaoKhongGuiQuaNhanhAsync(Guid userId, MucDichMa mucDich, bool nemLoi, CancellationToken ct)
        {
            DateTimeOffset? lanTruoc = await _ma.LanPhatGanNhatAsync(userId, mucDich, ct);
            if (lanTruoc.HasValue && _clock.GetUtcNow() - lanTruoc.Value < KhoangCachGuiLai)
            {
                if (nemLoi)
                {
                    throw new RuleViolationException("gui_qua_nhanh", "Vua gui email — thu lai sau " + (int)KhoangCachGuiLai.TotalSeconds + " giay.");
                }

                return false;
            }

            return true;
        }

        private void KiemMatKhauMoi(string? matKhau)
        {
            int toiThieu = _settings.SoNguyen(SettingKeys.IdentityPasswordMinLength);
            int toiDa = _settings.SoNguyen(SettingKeys.IdentityPasswordMaxLength);
            if (matKhau == null || matKhau.Length < toiThieu || matKhau.Length > toiDa)
            {
                throw new InvalidValueException("mat_khau_khong_hop_le", "Mat khau phai tu " + toiThieu + " den " + toiDa + " ky tu.");
            }
        }

        private async Task<MaTaiKhoan> LayMaAsync(string? maGoc, MucDichMa mucDich, CancellationToken ct)
        {
            MaTaiKhoan? ma = await _ma.TimMaHopLeAsync(maGoc, mucDich, ct);
            if (ma == null)
            {
                throw new InvalidValueException("ma_khong_hop_le", "Link khong hop le, da dung hoac da het han — hay xin link moi.");
            }

            return ma;
        }

        private async Task<User> LayUserAsync(Guid userId, CancellationToken ct)
        {
            User? user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user == null)
            {
                throw new NotFoundException("Khong tim thay tai khoan.");
            }

            return user;
        }
    }
}
