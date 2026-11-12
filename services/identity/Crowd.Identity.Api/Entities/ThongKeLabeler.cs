using System;

namespace Crowd.Identity.Api.Entities
{
    /// <summary>
    /// Cap do cua mot labeler (FL-12) — quyet dinh cua identity (luat identity.level_rules). Uy tin KHONG luu o day:
    /// doc view v_reputations cua quality (NguonUyTin, kien truc 3.32). So cau duoc chap nhan cung khong luu — doc qua
    /// gRPC LabelerStatsData tu project.attempts khi tinh cap (kien truc 3.24).
    /// </summary>
    public sealed class ThongKeLabeler
    {
        private ThongKeLabeler()
        {
        }

        public Guid UserId { get; private set; }

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

        /// <summary>Doi cap; tra ve true neu cap thay doi.</summary>
        public bool DatCap(int cap, DateTimeOffset luc)
        {
            if (cap == Level)
            {
                return false;
            }

            Level = cap;
            UpdatedAt = luc;
            return true;
        }
    }

    /// <summary>
    /// Hang cho TINH LAI CAP (kien truc 3.24): labeler vua co bai duoc chap nhan (project.grading_applied,
    /// khieu nai chap nhan) hoac vua doi uy tin. CapDoWorker doc theo lo (FOR UPDATE SKIP LOCKED), hoi so cau
    /// duoc chap nhan qua gRPC, tinh cap / huy hieu roi xoa dong. project-svc tam tat → dong nam lai, vong sau.
    /// </summary>
    public sealed class HangChoTinhCap
    {
        private HangChoTinhCap()
        {
        }

        public Guid UserId { get; private set; }

        public DateTimeOffset DirtyAt { get; private set; }

        /// <summary>Chuoi thao tac cua event gay ra (de level.changed / badge.awarded noi tiep); null = gop nhieu.</summary>
        public Guid? CorrelationId { get; private set; }

        /// <summary>Consumer ghi bang UPSERT SQL (CapDoService.XepTinhLaiAsync); ham nay cho seed (chua co dong nao).</summary>
        public static HangChoTinhCap Tao(Guid userId, DateTimeOffset luc)
        {
            HangChoTinhCap h = new HangChoTinhCap();
            h.UserId = userId;
            h.DirtyAt = luc;
            return h;
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
