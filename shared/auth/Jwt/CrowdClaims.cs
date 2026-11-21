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
        /// Hai su that MOT CHIEU ve tai khoan (da xac thuc / da duyet thi khong quay lai) — identity ghi vao
        /// access token, service khac doc tu day thay vi giu ban sao + nghe event:
        ///   email_verified — da bam link xac thuc email (FC-01): dieu kien rut tien, publish du an;
        ///   kyb_verified   — ho so doanh nghiep da duoc admin duyet (FB-02): dieu kien publish du an.
        /// Token cap TRUOC khi xac thuc / duyet chua co gia tri true → client lam moi token (POST /auth/refresh).
        /// </summary>
        public const string EmailVerified = "email_verified";
        public const string KybVerified = "kyb_verified";

        /// <summary>
        /// Xac minh danh tinh ca nhan (identity-svc, admin duyet anh giay to):
        ///   identity_verified — da xac minh: dieu kien vao du an rieng tu doi xac minh (FP-05);
        ///   adult             — da xac minh VA du tuoi (identity.adult_age) tinh tai luc phat token:
        ///                       dieu kien xem du an noi dung nhay cam (muc 2.9, VD-P-04).
        /// Tinh lai moi lan phat token nen nguoi vua du tuoi chi can lam moi token.
        /// </summary>
        public const string IdentityVerified = "identity_verified";
        public const string Adult = "adult";

        /// <summary>
        /// Cap do (identity) va diem uy tin 0-100 (quality, identity doc view v_reputations) TAI LUC PHAT token — kien
        /// truc 3.39. Chi dung de GOI Y (reco-svc loc du an chua du dieu kien ma khong hoi identity moi request); co the
        /// cu toi het doi token. Noi QUYET DINH (bat dau lam bai) van hoi identity, khong tin claim nay.
        /// Khong co claim reputation = chua co diem.
        /// </summary>
        public const string Level = "level";
        public const string Reputation = "reputation";

        /// <summary>Cap do trong token; token cu khong co claim → 1.</summary>
        public static int CapDo(ClaimsPrincipal user)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            int cap;
            return int.TryParse(user.FindFirstValue(Level), out cap) ? cap : 1;
        }

        /// <summary>Diem uy tin trong token; null = chua co diem.</summary>
        public static int? UyTin(ClaimsPrincipal user)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            int diem;
            return int.TryParse(user.FindFirstValue(Reputation), out diem) ? diem : null;
        }

        public static bool DaXacThucEmail(ClaimsPrincipal user)
        {
            return LaTrue(user, EmailVerified);
        }

        public static bool DaXacMinhDoanhNghiep(ClaimsPrincipal user)
        {
            return LaTrue(user, KybVerified);
        }

        public static bool DaXacMinhDanhTinh(ClaimsPrincipal user)
        {
            return LaTrue(user, IdentityVerified);
        }

        public static bool DuTuoi(ClaimsPrincipal user)
        {
            return LaTrue(user, Adult);
        }

        private static bool LaTrue(ClaimsPrincipal user, string claim)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            return string.Equals(user.FindFirstValue(claim), "true", StringComparison.OrdinalIgnoreCase);
        }

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
