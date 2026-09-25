using System.Collections.Generic;
using System.Net.Mail;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.Identity.Api.Dtos;
using Crowd.Identity.Api.Services;

namespace Crowd.Identity.Api.Helpers
{
    /// <summary>
    /// Kiem HINH DANG du lieu client gui len: co du truong khong, dai ngan the
    /// nao. Khong cham database — viec do la cua AuthService.
    ///
    /// Tach ra helper tinh de controller gon, va de test duoc ma khong can dung
    /// ca ASP.NET Core.
    /// </summary>
    public static class AuthRequestValidator
    {
        /// <summary>
        /// Tra ve danh sach loi theo tung truong (rong = hop le). Dang
        /// Dictionary nay dung la dang ma ValidationProblemDetails can.
        /// </summary>
        public static Dictionary<string, string[]> KiemDangKy(RegisterRequest? body)
        {
            Dictionary<string, string[]> loi = new Dictionary<string, string[]>();

            if (body == null)
            {
                loi["body"] = new string[] { "Thieu du lieu." };
                return loi;
            }

            MailAddress? diaChi;
            if (string.IsNullOrWhiteSpace(body.Email)
                || body.Email.Length > 320
                || !MailAddress.TryCreate(body.Email.Trim(), out diaChi))
            {
                loi["email"] = new string[] { "Email khong hop le." };
            }

            if (body.Password == null
                || body.Password.Length < MatKhauService.DoDaiToiThieu
                || body.Password.Length > MatKhauService.DoDaiToiDa)
            {
                loi["password"] = new string[]
                {
                    "Mat khau phai tu " + MatKhauService.DoDaiToiThieu + " den "
                    + MatKhauService.DoDaiToiDa + " ky tu.",
                };
            }

            if (string.IsNullOrWhiteSpace(body.DisplayName) || body.DisplayName.Trim().Length > 100)
            {
                loi["displayName"] = new string[] { "Ten hien thi tu 1 den 100 ky tu." };
            }

            if (body.Roles == null || body.Roles.Count == 0)
            {
                loi["roles"] = new string[] { "Phai chon it nhat mot vai tro." };
            }
            else
            {
                foreach (string vaiTro in body.Roles)
                {
                    // Chan tu cap quyen admin qua form dang ky.
                    if (vaiTro == null || !CrowdRoles.TuDangKyDuoc.Contains(vaiTro))
                    {
                        loi["roles"] = new string[]
                        {
                            "Vai tro chi duoc chon trong: business, labeler, sharer.",
                        };
                        break;
                    }
                }
            }

            return loi;
        }

        /// <summary>
        /// Dang nhap KHONG tra loi chi tiet theo truong nhu dang ky: thieu email
        /// hay mat khau qua dai deu chi la "sai thong tin" (VD-S-13).
        ///
        /// Gioi han do dai mat khau o day de khong ai bat server bam PBKDF2 tren
        /// mot chuoi 10 MB.
        /// </summary>
        public static bool LaDangNhapHopLe(LoginRequest? body)
        {
            if (body == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(body.Email))
            {
                return false;
            }

            if (string.IsNullOrEmpty(body.Password) || body.Password.Length > MatKhauService.DoDaiToiDa)
            {
                return false;
            }

            return true;
        }
    }
}
