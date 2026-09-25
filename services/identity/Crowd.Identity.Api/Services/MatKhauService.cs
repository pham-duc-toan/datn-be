using System;
using System.Collections.Generic;
using Crowd.Identity.Api.Entities;
using Microsoft.AspNetCore.Identity;

namespace Crowd.Identity.Api.Services
{
    /// <summary>
    /// Bam va kiem mat khau.
    ///
    /// Dung PasswordHasher cua ASP.NET Core — PBKDF2-HMAC-SHA512, 100.000 vong,
    /// muoi ngau nhien moi lan. KHONG tu viet ham bam mat khau: sai mot chi tiet
    /// nho (thieu muoi, so vong thap, so sanh chuoi khong dung thoi gian hang
    /// so) la hong ca he thong ma test van xanh.
    /// </summary>
    public sealed class MatKhauService
    {
        /// <summary>
        /// Gioi han tren. Khong co no, ke tan cong gui mat khau 10 MB va bat
        /// server chay 100.000 vong PBKDF2 tren 10 MB — mot kieu tan cong tu
        /// choi dich vu re tien.
        /// </summary>
        public const int DoDaiToiDa = 128;

        public const int DoDaiToiThieu = 8;

        private readonly IPasswordHasher<User> _hasher;
        private readonly User _userGia;
        private readonly string _hashGia;

        public MatKhauService(IPasswordHasher<User> hasher)
        {
            if (hasher == null)
            {
                throw new ArgumentNullException(nameof(hasher));
            }

            _hasher = hasher;

            // Mot hash mau, tinh MOT LAN luc khoi dong. Xem KiemGia().
            _userGia = User.Tao("khong-ton-tai@invalid", "gia", new List<string>(), DateTimeOffset.UnixEpoch);
            _hashGia = _hasher.HashPassword(_userGia, "mat-khau-gia-chi-de-can-thoi-gian");
        }

        public string Bam(User user, string matKhau)
        {
            return _hasher.HashPassword(user, matKhau);
        }

        public PasswordVerificationResult Kiem(User user, string matKhau)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            return _hasher.VerifyHashedPassword(user, user.PasswordHash, matKhau);
        }

        /// <summary>
        /// Goi khi email KHONG TON TAI. Van chay tron 100.000 vong PBKDF2 roi
        /// vut ket qua.
        ///
        /// Vi sao: neu email khong ton tai ma tra loi ngay trong 1ms, con email
        /// ton tai thi mat 80ms de kiem mat khau, ke tan cong chi can do thoi
        /// gian phan hoi la biet email nao co tai khoan — du hai truong hop tra
        /// ve cung mot thong bao loi.
        /// </summary>
        public void KiemGia(string matKhau)
        {
            _hasher.VerifyHashedPassword(_userGia, _hashGia, matKhau);
        }
    }
}
