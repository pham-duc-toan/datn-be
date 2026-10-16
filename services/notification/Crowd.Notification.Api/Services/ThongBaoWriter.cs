using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Notification.Api.Entities;
using Crowd.Notification.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Notification.Api.Services
{
    /// <summary>
    /// Ghi MOT thong bao trong transaction cua consumer (KHONG SaveChanges — IdempotencyGuard luu
    /// cung processed_events). Email chi DANH DAU "cho gui"; EmailWorker gui sau, loi thi thu lai —
    /// SMTP cham / sap khong lam nghen consumer (kien truc: "fan-out cham khong duoc chan ai").
    /// </summary>
    public sealed class ThongBaoWriter
    {
        private readonly NotificationDbContext _db;
        private readonly TimeProvider _clock;

        public ThongBaoWriter(NotificationDbContext db, TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _db = db;
            _clock = clock;
        }

        /// <summary>Chi gop vao thong bao co su kien moi nhat trong khoang nay (qua lau thi mo dong moi).</summary>
        public static readonly TimeSpan CuaSoGop = TimeSpan.FromHours(24);

        public async Task GhiAsync(Guid userId, string loai, NoiDung noiDung, CancellationToken ct)
        {
            if (noiDung == null)
            {
                throw new ArgumentNullException(nameof(noiDung));
            }

            LoaiThongBao l = TimLoai(loai);
            bool guiEmail = l.GuiEmail(await TuyChonAsync(userId, loai, ct));
            ThongBao t = ThongBao.Tao(userId, loai, noiDung.TheoNgonNgu(await NgonNguAsync(userId, ct)), guiEmail, _clock.GetUtcNow());
            _db.ThongBaos.Add(t);
            await BaoRealtimeAsync(userId, t.Id, ct);
        }

        /// <summary>Ngon ngu thong bao cua nguoi dung (FC-08); chua chon → tieng Viet.</summary>
        public async Task<string> NgonNguAsync(Guid userId, CancellationToken ct)
        {
            NgonNguNguoiDung? n = await _db.NgonNgus.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
            return n == null ? NgonNgu.TiengViet : n.Language;
        }

        /// <summary>
        /// Bao cac instance co WebSocket cua nguoi nay (RealtimeHub) — pg_notify trong CUNG transaction voi
        /// consumer: Postgres chi phat khi COMMIT, nen khong bao thong bao chua luu / bi rollback.
        /// </summary>
        private Task<int> BaoRealtimeAsync(Guid userId, Guid thongBaoId, CancellationToken ct)
        {
            return _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_notify({KenhRealtime}, {userId.ToString("N") + ":" + thongBaoId.ToString("N")})", ct);
        }

        /// <summary>Kenh LISTEN / NOTIFY cua thong bao moi.</summary>
        public const string KenhRealtime = "thong_bao_moi";

        /// <summary>
        /// Mot thong bao cho NHIEU nguoi (thong bao toan he thong, FM-09). Email chi khi
        /// <paramref name="choPhepEmail"/> (admin chon) VA tuy chon cua tung nguoi cho phep — tuy chon doc
        /// mot lan cho ca danh sach. Tra ve so thong bao da tao.
        /// </summary>
        public async Task<int> GhiHangLoatAsync(IReadOnlyList<Guid> userIds, string loai, NoiDung noiDung, bool choPhepEmail, CancellationToken ct)
        {
            if (userIds == null)
            {
                throw new ArgumentNullException(nameof(userIds));
            }

            if (noiDung == null)
            {
                throw new ArgumentNullException(nameof(noiDung));
            }

            LoaiThongBao l = TimLoai(loai);
            Dictionary<Guid, bool> tuyChon = await _db.TuyChonEmails.AsNoTracking()
                .Where(x => x.Type == loai && userIds.Contains(x.UserId))
                .ToDictionaryAsync(x => x.UserId, x => x.EmailEnabled, ct);
            Dictionary<Guid, string> ngonNgu = await _db.NgonNgus.AsNoTracking()
                .Where(x => userIds.Contains(x.UserId))
                .ToDictionaryAsync(x => x.UserId, x => x.Language, ct);
            DateTimeOffset bayGio = _clock.GetUtcNow();

            foreach (Guid u in userIds)
            {
                bool rieng;
                bool? chon = tuyChon.TryGetValue(u, out rieng) ? rieng : null;
                string? nn;
                ngonNgu.TryGetValue(u, out nn);
                ThongBao t = ThongBao.Tao(u, loai, noiDung.TheoNgonNgu(nn), choPhepEmail && l.GuiEmail(chon), bayGio);
                _db.ThongBaos.Add(t);
                await BaoRealtimeAsync(u, t.Id, ct);
            }

            return userIds.Count;
        }

        /// <summary>Email giao dich (xac thuc email, dat lai mat khau) — khong thanh thong bao trong ung dung.</summary>
        public void GhiEmailGiaoDich(Guid userId, string toEmail, string ten, string tieuDe, string noiDung)
        {
            _db.EmailGiaoDichs.Add(EmailGiaoDich.Tao(userId, toEmail, ten, tieuDe, noiDung, _clock.GetUtcNow()));
        }

        /// <summary>
        /// Email giao dich theo ngon ngu nguoi nhan (FC-08). userId null = nguoi chua co tai khoan (loi moi qua
        /// email) / dia chi van hanh — tieng Viet.
        /// </summary>
        public async Task GhiEmailGiaoDichAsync(Guid? userId, string toEmail, string ten, NoiDung email, CancellationToken ct)
        {
            if (email == null)
            {
                throw new ArgumentNullException(nameof(email));
            }

            NoiDung chon = userId.HasValue ? email.TheoNgonNgu(await NgonNguAsync(userId.Value, ct)) : email;
            _db.EmailGiaoDichs.Add(EmailGiaoDich.Tao(userId ?? Guid.Empty, toEmail, ten, chon.TieuDe, chon.NoiDungChinh, _clock.GetUtcNow()));
        }

        /// <summary>
        /// Loai ban nhieu (LoaiThongBao.Gop): CONG DON vao thong bao cung loai + cung duong dan con chua doc,
        /// email chua gui, cung che do email — mot sharer co nghin click/ngay chi thay MOT dong "Tien da kha
        /// dung" tang dan. Khoa dong FOR UPDATE (trong transaction cua IdempotencyGuard): hai instance khong
        /// cong mat nhau; EmailWorker dang gui dong do thi cho no xong, Postgres kiem lai dieu kien → dong da
        /// gui khong con khop → mo dong moi. `taoNoiDung(tong, soLan)` dung noi dung tu tong moi.
        /// </summary>
        public async Task GhiGopAsync(Guid userId, string loai, long soTien, Func<long, int, NoiDung> taoNoiDung, CancellationToken ct)
        {
            if (taoNoiDung == null)
            {
                throw new ArgumentNullException(nameof(taoNoiDung));
            }

            LoaiThongBao l = TimLoai(loai);
            if (!l.Gop)
            {
                throw new ArgumentException("Loai thong bao khong thuoc nhom gop: " + loai, nameof(loai));
            }

            bool guiEmail = l.GuiEmail(await TuyChonAsync(userId, loai, ct));
            string nn = await NgonNguAsync(userId, ct);
            DateTimeOffset bayGio = _clock.GetUtcNow();
            NoiDung motLan = taoNoiDung(soTien, 1).TheoNgonNgu(nn);
            string duongDan = motLan.DuongDan ?? string.Empty;
            string cheDo = guiEmail ? nameof(EmailState.Pending) : nameof(EmailState.Skipped);
            DateTimeOffset tu = bayGio - CuaSoGop;

            List<ThongBao> cu = await _db.ThongBaos
                .FromSqlInterpolated($@"SELECT * FROM notifications
                    WHERE user_id = {userId} AND type = {loai} AND COALESCE(link, '') = {duongDan}
                      AND read_at IS NULL AND email_state = {cheDo} AND created_at > {tu}
                    ORDER BY created_at DESC LIMIT 1 FOR UPDATE")
                .ToListAsync(ct);

            if (cu.Count == 0)
            {
                ThongBao moi = ThongBao.TaoGop(userId, loai, motLan, soTien, guiEmail, bayGio);
                _db.ThongBaos.Add(moi);
                await BaoRealtimeAsync(userId, moi.Id, ct);
                return;
            }

            ThongBao t = cu[0];
            long tong = (t.AmountVnd ?? 0) + soTien;
            t.GopThem(soTien, taoNoiDung(tong, t.MergedCount + 1).TheoNgonNgu(nn), bayGio);
            await BaoRealtimeAsync(userId, t.Id, ct);
        }

        private static LoaiThongBao TimLoai(string loai)
        {
            LoaiThongBao? l = LoaiThongBao.Tim(loai);
            if (l == null)
            {
                throw new ArgumentException("Loai thong bao khong co trong danh muc: " + loai, nameof(loai));
            }

            return l;
        }

        private async Task<bool?> TuyChonAsync(Guid userId, string loai, CancellationToken ct)
        {
            TuyChonEmail? tc = await _db.TuyChonEmails.AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == userId && x.Type == loai, ct);
            if (tc == null)
            {
                return null;
            }

            return tc.EmailEnabled;
        }
    }
}
