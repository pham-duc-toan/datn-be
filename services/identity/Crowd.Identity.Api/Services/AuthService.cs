using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.Contracts.Identity;
using Crowd.Identity.Api.Dtos;
using Crowd.Identity.Api.Entities;
using Crowd.Identity.Api.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crowd.Identity.Api.Services
{
    /// <summary>
    /// Nghiep vu dang ky, dang nhap, lam moi token, dang xuat.
    ///
    /// Dang ky SCOPED — cung vong doi voi IdentityDbContext va IOutboxWriter
    /// (moi request mot ban).
    ///
    /// Dau vao da duoc controller kiem hinh dang (AuthRequestValidator) truoc
    /// khi toi day; service chi lo nghiep vu.
    /// </summary>
    public sealed class AuthService
    {
        private readonly IdentityDbContext _db;
        private readonly IOutboxWriter _outbox;
        private readonly MatKhauService _matKhau;
        private readonly TokenIssuer _tokenIssuer;
        private readonly MaTaiKhoanService _maTaiKhoan;
        private readonly TimeProvider _clock;
        private readonly ILogger<AuthService> _logger;

        public AuthService(
            IdentityDbContext db,
            IOutboxWriter outbox,
            MatKhauService matKhau,
            TokenIssuer tokenIssuer,
            MaTaiKhoanService maTaiKhoan,
            TimeProvider clock,
            ILogger<AuthService> logger)
        {
            if (maTaiKhoan == null)
            {
                throw new ArgumentNullException(nameof(maTaiKhoan));
            }

            _maTaiKhoan = maTaiKhoan;

            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (outbox == null)
            {
                throw new ArgumentNullException(nameof(outbox));
            }

            if (matKhau == null)
            {
                throw new ArgumentNullException(nameof(matKhau));
            }

            if (tokenIssuer == null)
            {
                throw new ArgumentNullException(nameof(tokenIssuer));
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
            _matKhau = matKhau;
            _tokenIssuer = tokenIssuer;
            _clock = clock;
            _logger = logger;
        }

        // =====================================================================
        // DANG KY
        // =====================================================================

        public async Task<DangKyKetQua> DangKyAsync(
            RegisterRequest body,
            Guid correlationId,
            CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            string email = User.ChuanHoaEmail(body.Email!);

            // DUONG NHANH: bao loi dep ngay. Khong phai co che chan trung —
            // UNIQUE tren cot email moi la thu chan that (xem catch ben duoi).
            bool daCo = await _db.Users.AnyAsync(u => u.Email == email, ct);
            if (daCo)
            {
                return DangKyKetQua.EmailDaTonTai();
            }

            List<string> vaiTro = body.Roles!.Distinct(StringComparer.Ordinal).ToList();

            User user = User.Tao(email, body.DisplayName!.Trim(), vaiTro, _clock.GetUtcNow());
            user.DatPasswordHash(_matKhau.Bam(user, body.Password!));

            _db.Users.Add(user);

            // Phat user.registered trong CUNG transaction voi viec tao tai khoan.
            // Tai khoan co ma event mat -> khong ai gui email xac thuc.
            // Event co ma tai khoan khong -> email gui cho mot nguoi khong ton tai.
            // Outbox dam bao khong co truong hop nao trong hai truong hop do.
            UserRegistered ruot = new UserRegistered
            {
                UserId = user.Id,
                Email = user.Email,
                DisplayName = user.DisplayName,
                Roles = vaiTro,
            };

            // actor = null: truoc khi dang ky thanh cong thi chua co danh tinh nao
            // de gan hanh dong nay vao.
            _outbox.Enqueue(EventEnvelope.Create(
                producer: "identity-svc",
                correlationId: correlationId,
                payload: ruot));

            // Gui link xac thuc email (FC-01) cung transaction. Chua co danh tinh nen dung Caller he thong
            // chi de mang correlationId.
            await _maTaiKhoan.GuiXacThucEmailAsync(user, Caller.HeThong(correlationId, null), ct);

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex, "ux_users_email"))
            {
                // Hai nguoi dang ky cung email trong cung mot khoanh khac: ca hai
                // lot qua AnyAsync o tren, database chon ra mot nguoi.
                return DangKyKetQua.EmailDaTonTai();
            }

            return DangKyKetQua.ThanhCong(user.Id);
        }

        // =====================================================================
        // DANG NHAP
        // =====================================================================

        public async Task<DangNhapKetQua> DangNhapAsync(LoginRequest body, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            string email = User.ChuanHoaEmail(body.Email!);
            User? user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

            if (user == null)
            {
                // Van ton du thoi gian nhu khi kiem mat khau that — xem KiemGia().
                _matKhau.KiemGia(body.Password!);
                return DangNhapKetQua.SaiThongTin();
            }

            PasswordVerificationResult ketQua = _matKhau.Kiem(user, body.Password!);

            if (ketQua == PasswordVerificationResult.Failed)
            {
                return DangNhapKetQua.SaiThongTin();
            }

            // Bao "bi khoa" SAU khi mat khau dung, khong phai truoc. Nguoi da
            // biet mat khau thi biet tai khoan ton tai roi — khong lo them gi.
            if (user.Status == UserStatus.Blocked)
            {
                return DangNhapKetQua.BiKhoa();
            }

            // Thuat toan bam da nang cap (vd tang so vong) — tien tay bam lai theo
            // chuan moi, trong luc dang co mat khau goc trong tay.
            if (ketQua == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.DatPasswordHash(_matKhau.Bam(user, body.Password!));
            }

            // Moi lan dang nhap mo mot PHIEN moi (mot dong login_sessions).
            TokenResponse cap = await MoPhienAsync(user, ct);

            await _db.SaveChangesAsync(ct);

            return DangNhapKetQua.ThanhCong(cap);
        }

        // =====================================================================
        // LAM MOI TOKEN — xoay tai cho + phat hien dung lai
        // =====================================================================

        /// <summary>Tra null khi token khong dung duoc (controller doi thanh 401).</summary>
        public async Task<TokenResponse?> LamMoiAsync(string refreshTokenGoc, CancellationToken ct)
        {
            Guid phienId;
            if (!TokenIssuer.DocPhien(refreshTokenGoc, out phienId))
            {
                return null;
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            string hash = TokenIssuer.BamRefreshToken(refreshTokenGoc);

            var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                // KHOA DONG PHIEN: hai request lam moi cung luc cua cung mot phien xep hang o day —
                // cai sau thay token da doi (bai hoc VD-D-11: kiem va ghi phai nguyen tu).
                PhienDangNhap? phien = await _db.PhienDangNhaps
                    .FromSqlInterpolated($"SELECT * FROM login_sessions WHERE id = {phienId} FOR UPDATE")
                    .FirstOrDefaultAsync(ct);

                if (phien == null || !phien.ConDung(bayGio))
                {
                    return null;
                }

                if (!phien.LaTokenHienTai(hash))
                {
                    // Dung ma phien nhung KHONG phai token hien tai = mot token CU cua phien bi dem ra
                    // dung. It nhat hai ben dang giu (nguoi dung that va ke da danh cap) — khong phan
                    // biet duoc ai la ai nen thu hoi ca phien: ca hai phai dang nhap lai.
                    //
                    // Doi lai: hai tab cung lam moi bang mot token thi tab sau roi vao day va ca hai
                    // bi dang xuat. Uu tien an toan (frontend dong bo token giua cac tab).
                    phien.ThuHoi(bayGio);
                    await _db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);

                    _logger.LogWarning(
                        "Phat hien dung lai refresh token cua user {UserId}, phien {PhienId} — da thu hoi phien",
                        phien.UserId,
                        phien.Id);
                    return null;
                }

                User? user = await _db.Users.FirstOrDefaultAsync(u => u.Id == phien.UserId, ct);
                if (user == null || user.Status == UserStatus.Blocked)
                {
                    return null;
                }

                string refreshMoi = TokenIssuer.TaoRefreshToken(phien.Id);
                phien.Xoay(TokenIssuer.BamRefreshToken(refreshMoi), bayGio, _tokenIssuer.RefreshTokenLifetime);
                TokenResponse cap = await TaoResponseAsync(user, refreshMoi, ct);

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return cap;
            }
            finally
            {
                await tx.DisposeAsync();
            }
        }

        // =====================================================================
        // DANG XUAT
        // =====================================================================

        /// <summary>
        /// Thu hoi phien cua refresh token nay. Token khong hop le / phien khong con thi thoi —
        /// dang xuat la idempotent. Chi can dung MA PHIEN + hash khop token hien tai (token cu khong
        /// dang xuat ho duoc nguoi khac).
        ///
        /// Access token dang luu hanh con dung toi khi het han (toi da 15 phut); muon cat ngay thi
        /// dung "dang xuat moi noi" (PhienService — moc thu hoi phien o gateway).
        /// </summary>
        public async Task DangXuatAsync(string? refreshTokenGoc, CancellationToken ct)
        {
            Guid phienId;
            if (!TokenIssuer.DocPhien(refreshTokenGoc, out phienId))
            {
                return;
            }

            string hash = TokenIssuer.BamRefreshToken(refreshTokenGoc!);
            DateTimeOffset? luc = _clock.GetUtcNow();

            await _db.PhienDangNhaps
                .Where(p => p.Id == phienId && p.TokenHash == hash && p.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.RevokedAt, luc), ct);
        }

        /// <summary>
        /// Mo mot phien moi cho user (dang nhap; hoac vua doi mat khau: moi phien cu da bi thu hoi, tra
        /// lai phien moi cho chinh thiet bi dang doi). Chi them vao change tracker, KHONG SaveChanges —
        /// de noi goi quyet dinh transaction.
        /// </summary>
        public async Task<TokenResponse> MoPhienAsync(User user, CancellationToken ct)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            Guid phienId = Guid.CreateVersion7();
            string refreshGoc = TokenIssuer.TaoRefreshToken(phienId);
            _db.PhienDangNhaps.Add(PhienDangNhap.Mo(
                phienId, user.Id, TokenIssuer.BamRefreshToken(refreshGoc), _clock.GetUtcNow(), _tokenIssuer.RefreshTokenLifetime));

            return await TaoResponseAsync(user, refreshGoc, ct);
        }

        // =====================================================================
        // Ham dung chung
        // =====================================================================

        private async Task<TokenResponse> TaoResponseAsync(User user, string refreshToken, CancellationToken ct)
        {
            // Claim kyb_verified: project-svc kiem khi publish (khong giu ban sao). Chi tai khoan doanh nghiep moi co ho so.
            bool kybDaDuyet = user.Roles.Contains(CrowdRoles.Business)
                && await _db.BusinessProfiles.AnyAsync(p => p.UserId == user.Id && p.Status == KybStatus.Verified, ct);

            return new TokenResponse
            {
                AccessToken = _tokenIssuer.TaoAccessToken(user, kybDaDuyet),
                RefreshToken = refreshToken,
                TokenType = "Bearer",
                ExpiresIn = (int)_tokenIssuer.AccessTokenLifetime.TotalSeconds,
            };
        }
    }
}
