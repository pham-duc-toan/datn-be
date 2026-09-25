using System;
using System.Security.Claims;

namespace Crowd.BuildingBlocks.Auth.Jwt
{
    /// <summary>Ten claim dung trong token — mot cho duy nhat, khong go tay chuoi.</summary>
    public static class CrowdClaims
    {
        public const string Subject = "sub";
        public const string Email = "email";
        public const string Name = "name";
        public const string Role = "role";

        /// <summary>
        /// Lay userId tu token. Day la NGUON DANH TINH DUY NHAT duoc tin.
        ///
        /// VD-S-03: service KHONG BAO GIO lay userId tu duong dan hay body. Neu
        /// endpoint la /accounts/{id}/balance thi {id} chi de SO KHOP voi gia tri
        /// o day — lech thi 403 — chu khong phai de biet "ai dang hoi".
        /// </summary>
        public static Guid GetUserId(ClaimsPrincipal user)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            string? giaTri = user.FindFirstValue(Subject);

            Guid userId;
            if (giaTri == null || !Guid.TryParse(giaTri, out userId))
            {
                // Khong phai loi nguoi dung: da qua [Authorize] ma van thieu sub
                // nghia la cau hinh xac thuc sai. Phai no to, khong duoc im lang.
                throw new InvalidOperationException(
                    "Token hop le nhung khong co claim 'sub' dang Guid.");
            }

            return userId;
        }
    }
}
