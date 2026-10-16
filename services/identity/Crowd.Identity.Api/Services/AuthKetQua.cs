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

        /// <summary>Mat khau dung nhung tai khoan bat 2FA: tra ma thu thach, cho ma 6 so (FC-04).</summary>
        CanHaiLop,
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

        /// <summary>Chi khac null khi TrangThai = CanHaiLop.</summary>
        public TwoFactorChallengeResponse? ThuThach { get; private set; }

        public static DangNhapKetQua CanHaiLop(TwoFactorChallengeResponse thuThach)
        {
            if (thuThach == null)
            {
                throw new ArgumentNullException(nameof(thuThach));
            }

            DangNhapKetQua kq = new DangNhapKetQua(DangNhapTrangThai.CanHaiLop, null);
            kq.ThuThach = thuThach;
            return kq;
        }

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

namespace Crowd.Identity.Api.Services
{
    public enum XacThucHaiLopTrangThai
    {
        ThanhCong,

        /// <summary>Ma thu thach sai / het han / da dung → dang nhap lai bang mat khau.</summary>
        ThuThachKhongHopLe,

        /// <summary>Ma 6 so (hoac ma du phong) sai; con luot thu.</summary>
        SaiMa,

        /// <summary>Sai du so lan cho phep — thu thach bi huy.</summary>
        HetLuot,

        BiKhoa,
    }

    public sealed class XacThucHaiLopKetQua
    {
        private XacThucHaiLopKetQua(XacThucHaiLopTrangThai trangThai, TokenResponse? token, int conLai)
        {
            TrangThai = trangThai;
            Token = token;
            ConLai = conLai;
        }

        public XacThucHaiLopTrangThai TrangThai { get; }

        public TokenResponse? Token { get; }

        /// <summary>So lan thu con lai (chi co nghia khi SaiMa).</summary>
        public int ConLai { get; }

        public static XacThucHaiLopKetQua ThanhCong(TokenResponse token)
        {
            return new XacThucHaiLopKetQua(XacThucHaiLopTrangThai.ThanhCong, token, 0);
        }

        public static XacThucHaiLopKetQua Loi(XacThucHaiLopTrangThai trangThai)
        {
            return new XacThucHaiLopKetQua(trangThai, null, 0);
        }

        public static XacThucHaiLopKetQua SaiMa(int conLai)
        {
            return new XacThucHaiLopKetQua(XacThucHaiLopTrangThai.SaiMa, null, conLai);
        }
    }
}
