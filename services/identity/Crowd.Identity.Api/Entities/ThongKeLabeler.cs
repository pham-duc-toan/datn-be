using System;

namespace Crowd.Identity.Api.Entities
{
    /// <summary>
    /// Thong ke cua mot labeler de tinh cap do / huy hieu (FL-12): so nhan duoc duyet (tu
    /// annotation.approved), uy tin moi nhat (tu reputation.changed cua quality-svc).
    /// </summary>
    public sealed class ThongKeLabeler
    {
        private ThongKeLabeler()
        {
        }

        public Guid UserId { get; private set; }

        public int ApprovedCount { get; private set; }

        /// <summary>null = quality-svc chua cham (chua co bang chung).</summary>
        public int? Reputation { get; private set; }

        /// <summary>Moc thoi gian cua reputation.changed da ap dung — bo qua ban cu den tre.</summary>
        public DateTimeOffset? ReputationAt { get; private set; }

        public int Level { get; private set; }

        public DateTimeOffset UpdatedAt { get; private set; }

        public static ThongKeLabeler Tao(Guid userId, DateTimeOffset luc)
        {
            ThongKeLabeler t = new ThongKeLabeler();
            t.UserId = userId;
            t.Level = 1;
            t.UpdatedAt = luc;
            return t;
        }

        public void CongNhan(int soNhan, DateTimeOffset luc)
        {
            ApprovedCount = Math.Max(0, ApprovedCount + soNhan);
            UpdatedAt = luc;
        }

        /// <summary>Ghi uy tin moi; ban cu hon ban dang co thi bo qua (tra ve false).</summary>
        public bool DatUyTin(int uyTin, DateTimeOffset occurredAt)
        {
            if (ReputationAt.HasValue && occurredAt <= ReputationAt.Value)
            {
                return false;
            }

            Reputation = uyTin;
            ReputationAt = occurredAt;
            UpdatedAt = occurredAt;
            return true;
        }

        /// <summary>Doi cap; tra ve true neu cap thay doi.</summary>
        public bool DatCap(int cap)
        {
            if (cap == Level)
            {
                return false;
            }

            Level = cap;
            return true;
        }
    }

    /// <summary>So nhan duoc duyet theo NGAY — bang xep hang theo khoang thoi gian.</summary>
    public sealed class ThongKeNgay
    {
        private ThongKeNgay()
        {
        }

        public Guid UserId { get; private set; }

        public DateOnly Ngay { get; private set; }

        public int ApprovedCount { get; private set; }

        public static ThongKeNgay Tao(Guid userId, DateOnly ngay)
        {
            ThongKeNgay t = new ThongKeNgay();
            t.UserId = userId;
            t.Ngay = ngay;
            return t;
        }

        public void Cong(int soNhan)
        {
            ApprovedCount = Math.Max(0, ApprovedCount + soNhan);
        }
    }

    /// <summary>Huy hieu da dat — dat roi thi giu, khong thu hoi.</summary>
    public sealed class HuyHieuDaDat
    {
        private HuyHieuDaDat()
        {
            Badge = string.Empty;
        }

        public Guid UserId { get; private set; }

        public string Badge { get; private set; }

        public DateTimeOffset AwardedAt { get; private set; }

        public static HuyHieuDaDat Tao(Guid userId, string badge, DateTimeOffset luc)
        {
            HuyHieuDaDat h = new HuyHieuDaDat();
            h.UserId = userId;
            h.Badge = badge;
            h.AwardedAt = luc;
            return h;
        }
    }

    /// <summary>Moc thu hoi phien moi nhat cua user — gateway xin lai danh sach nay khi khoi dong.</summary>
    public sealed class MocThuHoiPhien
    {
        private MocThuHoiPhien()
        {
        }

        public Guid UserId { get; private set; }

        public DateTimeOffset RevokedAt { get; private set; }

        public static MocThuHoiPhien Tao(Guid userId, DateTimeOffset luc)
        {
            MocThuHoiPhien m = new MocThuHoiPhien();
            m.UserId = userId;
            m.RevokedAt = luc;
            return m;
        }

        public void DoiMoc(DateTimeOffset luc)
        {
            if (luc > RevokedAt)
            {
                RevokedAt = luc;
            }
        }
    }
}
