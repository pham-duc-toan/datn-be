using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Admin.Api.Entities;
using Crowd.Admin.Api.Persistence;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.Contracts.Admin;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Admin.Api.Services
{
    /// <summary>
    /// Viec can xu ly tay (kien truc 3.29, loai 2): admin xu ly ngoai he thong (chuyen tien tay, doi soat...) roi
    /// danh dau xong kem ghi chu. Dong muc ngay trong bang hang doi VA phat manual_action.resolved (nhat ky ghi
    /// ai xu ly; AuditConsumer ap event nay vao bang la khong lam gi vi muc da dong).
    /// </summary>
    public sealed class ViecTayService
    {
        public const int GhiChuToiDa = 1000;

        private readonly AdminDbContext _db;
        private readonly IOutboxWriter _outbox;
        private readonly TimeProvider _clock;

        public ViecTayService(AdminDbContext db, IOutboxWriter outbox, TimeProvider clock)
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

        public async Task DanhDauDaXuLyAsync(Guid actionId, string? ghiChu, Guid adminId, Guid correlationId, CancellationToken ct)
        {
            string note = (ghiChu ?? string.Empty).Trim();
            if (note.Length == 0 || note.Length > GhiChuToiDa)
            {
                throw new AdminException(400, "ghi_chu_khong_hop_le", "Ghi lại đã xử lý thế nào (1–" + GhiChuToiDa + " ký tự).");
            }

            DonDuyet? don = await _db.DonDuyets.AsNoTracking().FirstOrDefaultAsync(d => d.Kind == "manual" && d.SubjectId == actionId, ct);
            if (don == null)
            {
                throw new AdminException(404, "khong_tim_thay_viec", "Không tìm thấy việc cần xử lý này.");
            }

            if (don.Status != "pending")
            {
                throw new AdminException(409, "viec_da_xu_ly", "Việc này đã được đánh dấu xử lý xong.");
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
                    UPDATE approval_requests SET status = 'resolved', note = {note}, decided_at = {bayGio}, decided_by = {adminId}, last_event_at = {bayGio}
                    WHERE kind = 'manual' AND subject_id = {actionId} AND status = 'pending'", ct);

                _outbox.Enqueue(EventEnvelope.Create(
                    SettingService.Producer,
                    correlationId,
                    new ManualActionResolved { ActionId = actionId, Note = note },
                    null,
                    new EventActor(adminId, ActorRole.Admin)));
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            finally
            {
                await tx.DisposeAsync();
            }
        }
    }
}
