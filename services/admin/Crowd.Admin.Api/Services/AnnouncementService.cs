using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Admin.Api.Dtos;
using Crowd.Admin.Api.Entities;
using Crowd.Admin.Api.Persistence;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.Contracts.Admin;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Admin.Api.Services
{
    /// <summary>
    /// Thong bao toan he thong (FM-09): admin soan, admin-svc luu va phat system.announcement qua
    /// outbox (cung transaction); notification-svc tao thong bao cho tung nguoi dung thuoc doi tuong.
    /// </summary>
    public sealed class AnnouncementService
    {
        private static readonly string[] DoiTuong = new string[] { "all", "business", "labeler" };

        private readonly AdminDbContext _db;
        private readonly IOutboxWriter _outbox;
        private readonly TimeProvider _clock;

        public AnnouncementService(AdminDbContext db, IOutboxWriter outbox, TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (outbox == null)
            {
                throw new ArgumentNullException(nameof(outbox));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _db = db;
            _outbox = outbox;
            _clock = clock;
        }

        public async Task<AnnouncementResponse> GuiAsync(CreateAnnouncementRequest body, Guid adminId, Guid correlationId, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            string tieuDe = (body.Title ?? string.Empty).Trim();
            string noiDung = (body.Body ?? string.Empty).Trim();
            string doiTuong = (body.Audience ?? "all").Trim().ToLowerInvariant();
            if (tieuDe.Length == 0 || tieuDe.Length > Announcement.DoDaiTieuDeToiDa)
            {
                throw new AdminException(400, "tieu_de_khong_hop_le", "Tieu de tu 1 den " + Announcement.DoDaiTieuDeToiDa + " ky tu.");
            }

            if (noiDung.Length == 0 || noiDung.Length > Announcement.DoDaiNoiDungToiDa)
            {
                throw new AdminException(400, "noi_dung_khong_hop_le", "Noi dung tu 1 den " + Announcement.DoDaiNoiDungToiDa + " ky tu.");
            }

            if (!DoiTuong.Contains(doiTuong))
            {
                throw new AdminException(400, "doi_tuong_khong_hop_le", "audience: all | business | labeler.");
            }

            Announcement a = Announcement.Tao(tieuDe, noiDung, doiTuong, body.SendEmail, adminId, _clock.GetUtcNow());
            _db.Announcements.Add(a);
            _outbox.Enqueue(EventEnvelope.Create(
                SettingService.Producer,
                correlationId,
                new SystemAnnouncement
                {
                    AnnouncementId = a.Id,
                    Title = a.Title,
                    Body = a.Body,
                    Audience = a.Audience,
                    SendEmail = a.SendEmail,
                },
                null,
                new EventActor(adminId, ActorRole.Admin)));
            await _db.SaveChangesAsync(ct);
            return TaoResponse(a);
        }

        public async Task<IReadOnlyList<AnnouncementResponse>> DanhSachAsync(int limit, CancellationToken ct)
        {
            int soLuong = limit < 1 || limit > 100 ? 20 : limit;
            List<Announcement> ds = await _db.Announcements.AsNoTracking()
                .OrderByDescending(a => a.CreatedAt).Take(soLuong).ToListAsync(ct);
            return ds.Select(TaoResponse).ToList();
        }

        private static AnnouncementResponse TaoResponse(Announcement a)
        {
            return new AnnouncementResponse
            {
                Id = a.Id,
                Title = a.Title,
                Body = a.Body,
                Audience = a.Audience,
                SendEmail = a.SendEmail,
                CreatedBy = a.CreatedBy,
                CreatedAt = a.CreatedAt,
            };
        }
    }
}
