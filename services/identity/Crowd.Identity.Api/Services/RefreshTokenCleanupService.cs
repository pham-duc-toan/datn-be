using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Identity.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Identity.Api.Services
{
    /// <summary>
    /// Xoa PHIEN dang nhap (login_sessions) da het han hoac da thu hoi, va ma mot lan (xac thuc email /
    /// dat lai mat khau) het han. Moi lan dang nhap them mot phien; lam moi token xoay TAI CHO tren dong
    /// phien (khong them dong) nen bang chi lon theo so phien dang mo.
    ///
    /// KHONG DUNG DONG VOI LUONG REFRESH / DANG XUAT / KHOA TAI KHOAN:
    ///   - Cac luong do chi ghi vao phien CON DUNG (refresh tu choi phien het han / da thu hoi truoc
    ///     khi ghi; thu hoi chi cham phien expires_at > bay gio). Job chi xoa phien het han / thu hoi
    ///     tu truoc KhoangDem — hai tap dong tach roi, khong ai phai cho khoa cua ai.
    ///   - Them mot lop: FOR UPDATE SKIP LOCKED — dong nao dang bi transaction khac giu thi BO QUA,
    ///     vong sau xoa. Job khong bao gio dung cho, khong the deadlock voi request cua nguoi dung.
    ///   - Xoa theo lo KichThuocLo dong: moi cau DELETE ngan, khoa it dong trong thoi gian ngan.
    ///   - Nhieu instance identity cung chay: SKIP LOCKED chia dong cho nhau, khong xoa trung.
    /// </summary>
    public sealed class RefreshTokenCleanupService
    {
        public const int KichThuocLo = 1000;

        /// <summary>Chi xoa token het han truoc (bay gio - KhoangDem): bu lech dong ho giua cac instance.</summary>
        public static readonly TimeSpan KhoangDem = TimeSpan.FromHours(1);

        private readonly IdentityDbContext _db;

        public RefreshTokenCleanupService(IdentityDbContext db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            _db = db;
        }

        /// <summary>Xoa phien het han / da thu hoi truoc (<paramref name="bayGio"/> - KhoangDem). Tra ve so dong da xoa.</summary>
        public Task<int> XoaHetHanAsync(DateTimeOffset bayGio, CancellationToken ct)
        {
            DateTimeOffset moc = bayGio - KhoangDem;

            // LINQ khong viet duoc SKIP LOCKED nen dung SQL. Moi cau la mot transaction ngan.
            // Phien het han, hoac da thu hoi (dang xuat / dung lai token) qua khoang dem — khong con dung de lam gi.
            return XoaTheoLoAsync(() => _db.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE FROM login_sessions
                WHERE id IN (
                    SELECT id FROM login_sessions
                    WHERE expires_at < {moc} OR revoked_at < {moc}
                    LIMIT {KichThuocLo}
                    FOR UPDATE SKIP LOCKED)", ct));
        }

        /// <summary>Xoa ma mot lan (xac thuc email, dat lai mat khau) het han truoc (bayGio - KhoangDem).</summary>
        public Task<int> XoaMaTaiKhoanHetHanAsync(DateTimeOffset bayGio, CancellationToken ct)
        {
            DateTimeOffset moc = bayGio - KhoangDem;
            return XoaTheoLoAsync(() => _db.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE FROM account_tokens
                WHERE id IN (
                    SELECT id FROM account_tokens
                    WHERE expires_at < {moc}
                    ORDER BY expires_at
                    LIMIT {KichThuocLo}
                    FOR UPDATE SKIP LOCKED)", ct));
        }

        /// <summary>Goi cau xoa lap lai toi khi mot lo khong day (het dong, hoac dong con lai dang bi khoa).</summary>
        private static async Task<int> XoaTheoLoAsync(Func<Task<int>> xoaMotLo)
        {
            int tong = 0;
            int lanNay;
            do
            {
                lanNay = await xoaMotLo();
                tong = tong + lanNay;
            }
            while (lanNay == KichThuocLo);

            return tong;
        }
    }
}
