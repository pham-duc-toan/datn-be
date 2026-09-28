using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        private readonly TimeProvider _clock;
        private readonly ILogger<AuthService> _logger;

        public AuthService(
            IdentityDbContext db,
            IOutboxWriter outbox,
            MatKhauService matKhau,
            TokenIssuer tokenIssuer,
            TimeProvider clock,
            ILogger<AuthService> logger)
        {
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

            // Moi lan dang nhap mo mot HO token moi.
            Guid khongDung;
            TokenResponse cap = PhatCapToken(user, Guid.CreateVersion7(), out khongDung);

            await _db.SaveChangesAsync(ct);

            return DangNhapKetQua.ThanhCong(cap);
        }

        // =====================================================================
        // LAM MOI TOKEN — xoay vong + phat hien dung lai
        // =====================================================================

        /// <summary>Tra null khi token khong dung duoc (controller doi thanh 401).</summary>
        public async Task<TokenResponse?> LamMoiAsync(string refreshTokenGoc, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(refreshTokenGoc))
            {
                return null;
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            string hash = TokenIssuer.BamRefreshToken(refreshTokenGoc);

            RefreshToken? cu = await _db.RefreshTokens
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

            if (cu == null)
            {
                return null;
            }

            if (cu.RevokedAt != null)
            {
                // TOKEN DA XOAY ma van bi dem ra dung. Nghia la it nhat hai ben dang
                // giu no — nguoi dung that va ke da danh cap. Khong phan biet duoc
                // ai la ai, nen thu hoi CA HO: ca hai phai dang nhap lai, va ke
                // trom mat quyen truy cap.
                await ThuHoiCaHoAsync(cu.FamilyId, bayGio, ct);

                _logger.LogWarning(
                    "Phat hien dung lai refresh token cua user {UserId}, ho {FamilyId} — da thu hoi ca ho",
                    cu.UserId,
                    cu.FamilyId);

                return null;
            }

            if (cu.ExpiresAt <= bayGio)
            {
                return null;
            }

            User? user = await _db.Users.FirstOrDefaultAsync(u => u.Id == cu.UserId, ct);
            if (user == null || user.Status == UserStatus.Blocked)
            {
                return null;
            }

            var tx = await _db.Database.BeginTransactionAsync(ct);

            try
            {
                Guid idMoi;
                TokenResponse cap = PhatCapToken(user, cu.FamilyId, out idMoi);

                // DANH DAU TOKEN CU DA DUNG — NGUYEN TU.
                //
                // Dieu kien "RevokedAt IS NULL" nam TRONG cau UPDATE, dung bai hoc
                // VD-D-11. Hai request refresh cung luc voi cung mot token: ca hai
                // doc thay "chua thu hoi" o tren, nhung chi MOT cai cap nhat duoc
                // dong nay. Cai kia nhan 0 dong va roi vao nhanh dung lai.
                DateTimeOffset? thuHoiLuc = bayGio;
                Guid? thayBang = idMoi;

                int soDong = await _db.RefreshTokens
                    .Where(t => t.Id == cu.Id && t.RevokedAt == null)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(t => t.RevokedAt, thuHoiLuc)
                              .SetProperty(t => t.ReplacedById, thayBang),
                        ct);

                if (soDong == 0)
                {
                    // Mot request khac vua xoay token nay truoc mot tich tac.
                    // Chon cach NGHIEM: coi nhu dung lai, thu hoi ca ho.
                    //
                    // Doi lai: nguoi dung mo hai tab cung refresh cung luc se bi
                    // dang xuat ca hai. Mot so he thong cho mot "thoi gian an han"
                    // vai giay de tranh dieu nay; o day uu tien an toan.
                    await tx.RollbackAsync(ct);
                    _db.ChangeTracker.Clear();
                    await ThuHoiCaHoAsync(cu.FamilyId, bayGio, ct);
                    return null;
                }

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
        /// Thu hoi ca ho cua refresh token nay. Token khong ton tai thi thoi —
        /// dang xuat la idempotent.
        ///
        /// Luu y: access token dang luu hanh VAN DUNG DUOC toi khi het han (toi
        /// da 15 phut). Dang xuat chi chan viec LAM MOI.
        /// </summary>
        public async Task DangXuatAsync(string? refreshTokenGoc, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(refreshTokenGoc))
            {
                return;
            }

            string hash = TokenIssuer.BamRefreshToken(refreshTokenGoc);

            RefreshToken? token = await _db.RefreshTokens
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

            if (token != null)
            {
                await ThuHoiCaHoAsync(token.FamilyId, _clock.GetUtcNow(), ct);
            }
        }

        // =====================================================================
        // Ham dung chung
        // =====================================================================

        /// <summary>
        /// Tao access token + refresh token moi trong ho <paramref name="hoToken"/>.
        /// Chi them vao change tracker, KHONG SaveChanges — de noi goi quyet dinh
        /// transaction.
        /// </summary>
        private TokenResponse PhatCapToken(User user, Guid hoToken, out Guid idRefreshMoi)
        {
            string refreshGoc = TokenIssuer.TaoRefreshTokenGoc();

            RefreshToken moi = RefreshToken.Tao(
                user.Id,
                TokenIssuer.BamRefreshToken(refreshGoc),
                hoToken,
                _clock.GetUtcNow(),
                _tokenIssuer.RefreshTokenLifetime);

            _db.RefreshTokens.Add(moi);
            idRefreshMoi = moi.Id;

            return new TokenResponse
            {
                AccessToken = _tokenIssuer.TaoAccessToken(user),
                RefreshToken = refreshGoc,
                TokenType = "Bearer",
                ExpiresIn = (int)_tokenIssuer.AccessTokenLifetime.TotalSeconds,
            };
        }

        private Task<int> ThuHoiCaHoAsync(Guid hoToken, DateTimeOffset luc, CancellationToken ct)
        {
            DateTimeOffset? thuHoiLuc = luc;

            return _db.RefreshTokens
                .Where(t => t.FamilyId == hoToken && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, thuHoiLuc), ct);
        }
    }
}
