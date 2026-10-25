using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Notification.Api.Dtos;
using Crowd.Notification.Api.Entities;
using Crowd.Notification.Api.Exceptions;
using Crowd.Notification.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Notification.Api.Services
{
    /// <summary>API cua nguoi dung voi thong bao CUA CHINH MINH (moi truy van loc theo userId tu token).</summary>
    public sealed class ThongBaoService
    {
        private readonly NotificationDbContext _db;
        private readonly TimeProvider _clock;

        public ThongBaoService(NotificationDbContext db, TimeProvider clock)
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

        public async Task<NotificationPageResponse> DanhSachAsync(Guid userId, bool chiChuaDoc, int page, int pageSize, CancellationToken ct)
        {
            int trang = page < 1 ? 1 : page;
            int co = pageSize < 1 || pageSize > 100 ? 20 : pageSize;

            IQueryable<ThongBao> q = _db.ThongBaos.AsNoTracking().Where(t => t.UserId == userId);
            if (chiChuaDoc)
            {
                q = q.Where(t => t.ReadAt == null);
            }

            int tong = await q.CountAsync(ct);
            List<ThongBao> ds = await q.OrderByDescending(t => t.CreatedAt).Skip((trang - 1) * co).Take(co).ToListAsync(ct);

            List<NotificationResponse> items = new List<NotificationResponse>();
            foreach (ThongBao t in ds)
            {
                items.Add(new NotificationResponse
                {
                    Id = t.Id,
                    Type = t.Type,
                    Title = t.Title,
                    Body = t.Body,
                    Link = t.Link,
                    CreatedAt = t.CreatedAt,
                    ReadAt = t.ReadAt,
                });
            }

            return new NotificationPageResponse
            {
                Items = items,
                Page = trang,
                PageSize = co,
                Total = tong,
                UnreadCount = await DemChuaDocAsync(userId, ct),
            };
        }

        public Task<int> DemChuaDocAsync(Guid userId, CancellationToken ct)
        {
            return _db.ThongBaos.CountAsync(t => t.UserId == userId && t.ReadAt == null, ct);
        }

        public async Task DaDocAsync(Guid userId, Guid id, CancellationToken ct)
        {
            ThongBao? t = await _db.ThongBaos.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
            if (t == null)
            {
                throw new NotFoundException("Không tìm thấy thông báo.");
            }

            if (t.DanhDauDaDoc(_clock.GetUtcNow()))
            {
                await _db.SaveChangesAsync(ct);
            }
        }

        public Task<int> DocHetAsync(Guid userId, CancellationToken ct)
        {
            DateTimeOffset bayGio = _clock.GetUtcNow();
            return _db.ThongBaos
                .Where(t => t.UserId == userId && t.ReadAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ReadAt, bayGio), ct);
        }

        public async Task<IReadOnlyList<PreferenceResponse>> TuyChonAsync(Guid userId, CancellationToken ct)
        {
            Dictionary<string, bool> rieng = await _db.TuyChonEmails.AsNoTracking()
                .Where(x => x.UserId == userId).ToDictionaryAsync(x => x.Type, x => x.EmailEnabled, ct);

            List<PreferenceResponse> ds = new List<PreferenceResponse>();
            foreach (LoaiThongBao l in LoaiThongBao.TatCa)
            {
                bool bat;
                bool? r = null;
                if (rieng.TryGetValue(l.Ma, out bat))
                {
                    r = bat;
                }

                ds.Add(new PreferenceResponse { Type = l.Ma, Label = l.Ten, Mandatory = l.BatBuoc, EmailEnabled = l.GuiEmail(r) });
            }

            return ds;
        }

        public async Task<IReadOnlyList<PreferenceResponse>> DatTuyChonAsync(Guid userId, UpdatePreferencesRequest body, CancellationToken ct)
        {
            if (body == null || body.Items == null || body.Items.Count == 0)
            {
                throw new InvalidValueException("thieu_tuy_chon", "Cần danh sách items [{ type, emailEnabled }].");
            }

            foreach (PreferenceItem i in body.Items)
            {
                LoaiThongBao? l = LoaiThongBao.Tim(i.Type ?? string.Empty);
                if (l == null)
                {
                    throw new InvalidValueException("loai_khong_hop_le", "Loại thông báo không tồn tại: " + i.Type + ".");
                }

                if (l.BatBuoc && !i.EmailEnabled)
                {
                    throw new InvalidValueException("loai_bat_buoc", "Email loại " + l.Ma + " là bắt buộc, không tắt được.");
                }

                TuyChonEmail? tc = await _db.TuyChonEmails.FirstOrDefaultAsync(x => x.UserId == userId && x.Type == l.Ma, ct);
                if (tc == null)
                {
                    _db.TuyChonEmails.Add(TuyChonEmail.Tao(userId, l.Ma, i.EmailEnabled));
                }
                else
                {
                    tc.Dat(i.EmailEnabled);
                }
            }

            await _db.SaveChangesAsync(ct);
            return await TuyChonAsync(userId, ct);
        }
    }
}
