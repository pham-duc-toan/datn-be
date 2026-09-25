using System;
using Crowd.Identity.Api.Dtos;

namespace Crowd.Identity.Api.Services
{
    // Ket qua AuthService tra ve cho controller.
    //
    // Service KHONG tra IActionResult: no khong biet gi ve HTTP. Service chi noi
    // "chuyen gi da xay ra", con controller quyet dinh doi chuyen do thanh ma
    // HTTP nao (201, 401, 409...).

    public enum DangKyTrangThai
    {
        ThanhCong,
        EmailDaTonTai,
    }

    public sealed class DangKyKetQua
    {
        private DangKyKetQua(DangKyTrangThai trangThai, Guid userId)
        {
            TrangThai = trangThai;
            UserId = userId;
        }

        public DangKyTrangThai TrangThai { get; }

        /// <summary>Chi co nghia khi TrangThai = ThanhCong.</summary>
        public Guid UserId { get; }

        public static DangKyKetQua ThanhCong(Guid userId)
        {
            return new DangKyKetQua(DangKyTrangThai.ThanhCong, userId);
        }

        public static DangKyKetQua EmailDaTonTai()
        {
            return new DangKyKetQua(DangKyTrangThai.EmailDaTonTai, Guid.Empty);
        }
    }

    public enum DangNhapTrangThai
    {
        ThanhCong,

        /// <summary>Email khong ton tai HOAC mat khau sai — co y gop lam mot.</summary>
        SaiThongTin,

        BiKhoa,
    }

    public sealed class DangNhapKetQua
    {
        private DangNhapKetQua(DangNhapTrangThai trangThai, TokenResponse? token)
        {
            TrangThai = trangThai;
            Token = token;
        }

        public DangNhapTrangThai TrangThai { get; }

        /// <summary>Chi khac null khi TrangThai = ThanhCong.</summary>
        public TokenResponse? Token { get; }

        public static DangNhapKetQua ThanhCong(TokenResponse token)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            return new DangNhapKetQua(DangNhapTrangThai.ThanhCong, token);
        }

        public static DangNhapKetQua SaiThongTin()
        {
            return new DangNhapKetQua(DangNhapTrangThai.SaiThongTin, null);
        }

        public static DangNhapKetQua BiKhoa()
        {
            return new DangNhapKetQua(DangNhapTrangThai.BiKhoa, null);
        }
    }
}
