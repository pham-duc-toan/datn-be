using System;

namespace Crowd.Admin.Api.Entities
{
    /// <summary>
    /// Mot VIEC CAN XU LY TAY (kien truc 3.29, loai 2; bang rieng tu 3.33): he thong khong tu xu ly duoc (vd thuong
    /// khong chi duoc vi du an khong co ky quy), admin lam ngoai he thong roi danh dau xong. Du lieu RIENG cua admin-svc
    /// — dung tu manual_action.required, khong phai ban sao cua service nao. Ghi bang SQL trong ViecTayService.
    /// </summary>
    public sealed class ViecTay
    {
        private ViecTay()
        {
            Kind = string.Empty;
            Title = string.Empty;
            Detail = string.Empty;
            Status = string.Empty;
        }

        /// <summary>actionId cua manual_action.required (khoa chinh — giao lai khong tao trung).</summary>
        public Guid ActionId { get; private set; }

        /// <summary>Ma loai viec on dinh (vd thuong_khong_chi_duoc) — de loc.</summary>
        public string Kind { get; private set; }

        public string Title { get; private set; }

        /// <summary>Mo ta viec can lam, loi thuong cho admin.</summary>
        public string Detail { get; private set; }

        public Guid? ProjectId { get; private set; }

        /// <summary>Nguoi lien quan (nguoi lam, doanh nghiep...).</summary>
        public Guid? UserId { get; private set; }

        public long? AmountVnd { get; private set; }

        /// <summary>pending | resolved</summary>
        public string Status { get; private set; }

        /// <summary>Admin ghi da xu ly the nao.</summary>
        public string? Note { get; private set; }

        public DateTimeOffset OpenedAt { get; private set; }

        public DateTimeOffset? ResolvedAt { get; private set; }

        public Guid? ResolvedBy { get; private set; }
    }
}
