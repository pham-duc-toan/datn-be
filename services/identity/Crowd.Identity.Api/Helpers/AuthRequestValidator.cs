using System.Collections.Generic;
using System.Net.Mail;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Settings;
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
        public static Dictionary<string, string[]> KiemDangKy(RegisterRequest? body, ISettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            Dictionary<string, string[]> loi = new Dictionary<string, string[]>();

            if (body == null)
            {
                loi["body"] = new string[] { "Thiếu dữ liệu." };
                return loi;
            }

            MailAddress? diaChi;
            if (string.IsNullOrWhiteSpace(body.Email)
                || body.Email.Length > 320
                || !MailAddress.TryCreate(body.Email.Trim(), out diaChi))
            {
                loi["email"] = new string[] { "Email không hợp lệ." };
            }

            // Gioi han TREN chan tan cong tu choi dich vu: gui mat khau 10 MB bat
            // server chay 100.000 vong PBKDF2 tren 10 MB. Setting identity.password_*.
            int toiThieu = settings.SoNguyen(SettingKeys.IdentityPasswordMinLength);
            int toiDa = settings.SoNguyen(SettingKeys.IdentityPasswordMaxLength);
            if (body.Password == null
                || body.Password.Length < toiThieu
                || body.Password.Length > toiDa)
            {
                loi["password"] = new string[]
                {
                    "Mật khẩu phải từ " + toiThieu + " đến "
                    + toiDa + " ký tự.",
                };
            }

            string? loiUsername = Crowd.Identity.Api.Entities.User.LoiUsername(body.Username);
            if (loiUsername != null)
            {
                loi["username"] = new string[] { loiUsername };
            }

            if (string.IsNullOrWhiteSpace(body.DisplayName) || body.DisplayName.Trim().Length > 100)
            {
                loi["displayName"] = new string[] { "Tên hiển thị từ 1 đến 100 ký tự." };
            }

            if (body.Roles == null || body.Roles.Count == 0)
            {
                loi["roles"] = new string[] { "Phải chọn ít nhất một vai trò." };
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
                            "Vai trò chỉ được chọn trong: doanh nghiệp, người gán nhãn (chia sẻ link kiếm tiền là chức năng của người gán nhãn).",
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
        public static bool LaDangNhapHopLe(LoginRequest? body, ISettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (body == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(body.Email))
            {
                return false;
            }

            if (string.IsNullOrEmpty(body.Password) || body.Password.Length > settings.SoNguyen(SettingKeys.IdentityPasswordMaxLength))
            {
                return false;
            }

            return true;
        }
    }
}
