using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Identity.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Identity.Api.Services
{
    /// <summary>
    /// Xoa refresh token DA HET HAN, va ma mot lan (xac thuc email / dat lai mat khau) het han.
    /// Moi lan dang nhap / refresh them mot dong vao refresh_tokens va khong ai xoa — khong don thi
    /// bang chi phinh ra.
    ///
    /// CHI xoa dong het han. Dong da thu hoi ma CHUA het han phai giu lai: AuthService.LamMoiAsync
    /// can no de nhan ra token cu bi dem ra dung lai (va thu hoi ca ho).
    ///
    /// KHONG DUNG DONG VOI LUONG REFRESH / DANG XUAT / KHOA TAI KHOAN:
    ///   - Cac luong do chi ghi vao dong CON HAN (refresh tu choi token het han truoc khi ghi;
    ///     thu hoi chi cham dong expires_at > bay gio). Job chi xoa dong het han tu truoc
    ///     KhoangDem — hai tap dong tach roi, khong ai phai cho khoa cua ai.
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

        /// <summary>Xoa refresh token het han truoc (<paramref name="bayGio"/> - KhoangDem). Tra ve so dong da xoa.</summary>
        public Task<int> XoaHetHanAsync(DateTimeOffset bayGio, CancellationToken ct)
        {
            DateTimeOffset moc = bayGio - KhoangDem;

            // LINQ khong viet duoc SKIP LOCKED nen dung SQL. Moi cau la mot transaction ngan.
            return XoaTheoLoAsync(() => _db.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE FROM refresh_tokens
                WHERE id IN (
                    SELECT id FROM refresh_tokens
                    WHERE expires_at < {moc}
                    ORDER BY expires_at
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
