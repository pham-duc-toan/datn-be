using System;
using System.Collections.Generic;

namespace Crowd.BuildingBlocks.Auth.Jwt
{
    /// <summary>
    /// Vai tro trong he thong — muc 1.2 cua dac ta. Chuoi chu thuong, khop voi
    /// ActorRole trong envelope va voi claim "role" trong token.
    /// </summary>
    public static class CrowdRoles
    {
        public const string Business = "business";
        public const string Labeler = "labeler";
        public const string Sharer = "sharer";
        public const string Admin = "admin";

        /// <summary>
        /// Vai tro NGUOI DUNG TU CHON duoc luc dang ky (FC-01).
        /// Admin khong nam trong danh sach nay — admin chi duoc cap boi admin khac.
        /// Sharer cung khong: chia se link la chuc nang cua labeler, khong can vai tro rieng.
        /// Hang Sharer van giu cho ActorRole trong event (nguoi tao link / nhan tien vuot link)
        /// va cho token cua tai khoan cu da dang ky voi vai tro nay.
        /// </summary>
        public static readonly IReadOnlySet<string> TuDangKyDuoc =
            new HashSet<string>(StringComparer.Ordinal) { Business, Labeler };
    }
}
