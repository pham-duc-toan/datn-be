using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
    /// Viec can xu ly tay (kien truc 3.29, loai 2). Tu 3.33 nam o bang rieng manual_actions — hang doi duyet chung
    /// (approval_requests, read model gom tu event cua 7 loai don) da bo: moi loai don duyet o trang rieng, doc thang
    /// API cua service chu.
    ///   - AuditConsumer goi ApDungAsync voi moi event lan dau thay: chi manual_action.required / .resolved co tac dung.
    ///   - Admin xu ly ngoai he thong (chuyen tien tay, doi soat...) roi danh dau xong kem ghi chu → phat
    ///     manual_action.resolved (nhat ky ghi ai xu ly; ap lai event nay la khong lam gi vi viec da dong).
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

        // =====================================================================
        // Ghi tu event (trong transaction cua AuditConsumer)
        // =====================================================================

        /// <summary>Ap dung MOT event. Khong phai event viec tay → khong lam gi. Tra true neu co thay doi.</summary>
        public static async Task<bool> ApDungAsync(
            AdminDbContext db, string eventType, string payloadJson, DateTimeOffset luc, Guid? actorId, CancellationToken ct)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (eventType != ManualActionRequired.EventType && eventType != ManualActionResolved.EventType)
            {
                return false;
            }

            using (JsonDocument doc = JsonDocument.Parse(payloadJson))
            {
                JsonElement p = doc.RootElement;
                Guid actionId = p.GetProperty("actionId").GetGuid();
                if (eventType == ManualActionRequired.EventType)
                {
                    string kind = Cat(Chuoi(p, "kind") ?? "khac", 60);
                    string title = Cat(Chuoi(p, "title") ?? string.Empty, 300);
                    string detail = Cat(Chuoi(p, "description") ?? string.Empty, 1000);
                    Guid? projectId = IdHoacNull(p, "projectId");
                    Guid? userId = IdHoacNull(p, "userId");
                    long? amount = So(p, "amountVnd");
                    return await db.Database.ExecuteSqlInterpolatedAsync($@"
                        INSERT INTO manual_actions (action_id, kind, title, detail, project_id, user_id, amount_vnd, status, opened_at)
                        VALUES ({actionId}, {kind}, {title}, {detail}, {projectId}, {userId}, {amount}, 'pending', {luc})
                        ON CONFLICT (action_id) DO NOTHING", ct) > 0;
                }

                // manual_action.resolved: thuong la chinh admin-svc vua dong (DanhDauDaXuLyAsync) → khong con gi lam.
                string? note = Chuoi(p, "note");
                string? ghiChu = note == null ? null : Cat(note, GhiChuToiDa);
                return await db.Database.ExecuteSqlInterpolatedAsync($@"
                    UPDATE manual_actions SET status = 'resolved', note = COALESCE({ghiChu}, note), resolved_at = {luc}, resolved_by = {actorId}
                    WHERE action_id = {actionId} AND status = 'pending'", ct) > 0;
            }
        }

        // =====================================================================
        // Admin
        // =====================================================================

        /// <summary>status: pending (mac dinh) | resolved | all. Moi nhat truoc.</summary>
        public async Task<PagedResponse<ManualActionResponse>> DanhSachAsync(string? status, int page, int pageSize, CancellationToken ct)
        {
            int trang = page < 1 ? 1 : page;
            int co = pageSize < 1 || pageSize > 200 ? 50 : pageSize;
            string tt = string.IsNullOrWhiteSpace(status) ? "pending" : status.Trim().ToLowerInvariant();
            if (tt != "pending" && tt != "resolved" && tt != "all")
            {
                throw new AdminException(400, "trang_thai_khong_hop_le", "status: pending | resolved | all.");
            }

            IQueryable<ViecTay> q = _db.ViecTays.AsNoTracking();
            if (tt != "all")
            {
                q = q.Where(v => v.Status == tt);
            }

            int tong = await q.CountAsync(ct);
            List<ViecTay> ds = await q.OrderByDescending(v => v.OpenedAt).ThenByDescending(v => v.ActionId)
                .Skip((trang - 1) * co).Take(co).ToListAsync(ct);
            return new PagedResponse<ManualActionResponse>
            {
                Items = ds.Select(TaoResponse).ToList(),
                Page = trang,
                PageSize = co,
                Total = tong,
            };
        }

        public async Task DanhDauDaXuLyAsync(Guid actionId, string? ghiChu, Guid adminId, Guid correlationId, CancellationToken ct)
        {
            string note = (ghiChu ?? string.Empty).Trim();
            if (note.Length == 0 || note.Length > GhiChuToiDa)
            {
                throw new AdminException(400, "ghi_chu_khong_hop_le", "Ghi lại đã xử lý thế nào (1–" + GhiChuToiDa + " ký tự).");
            }

            ViecTay? viec = await _db.ViecTays.AsNoTracking().FirstOrDefaultAsync(v => v.ActionId == actionId, ct);
            if (viec == null)
            {
                throw new AdminException(404, "khong_tim_thay_viec", "Không tìm thấy việc cần xử lý này.");
            }

            if (viec.Status != "pending")
            {
                throw new AdminException(409, "viec_da_xu_ly", "Việc này đã được đánh dấu xử lý xong.");
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
                    UPDATE manual_actions SET status = 'resolved', note = {note}, resolved_at = {bayGio}, resolved_by = {adminId}
                    WHERE action_id = {actionId} AND status = 'pending'", ct);

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

        private static ManualActionResponse TaoResponse(ViecTay v)
        {
            return new ManualActionResponse
            {
                ActionId = v.ActionId,
                Kind = v.Kind,
                Title = v.Title,
                Detail = v.Detail,
                ProjectId = v.ProjectId,
                UserId = v.UserId,
                AmountVnd = v.AmountVnd,
                Status = v.Status,
                Note = v.Note,
                OpenedAt = v.OpenedAt,
                ResolvedAt = v.ResolvedAt,
                ResolvedBy = v.ResolvedBy,
            };
        }

        private static string Cat(string s, int toiDa)
        {
            return s.Length > toiDa ? s.Substring(0, toiDa) : s;
        }

        private static string? Chuoi(JsonElement p, string ten)
        {
            JsonElement v;
            if (!p.TryGetProperty(ten, out v) || v.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            return v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText();
        }

        private static Guid? IdHoacNull(JsonElement p, string ten)
        {
            JsonElement v;
            return p.TryGetProperty(ten, out v) && v.ValueKind == JsonValueKind.String ? v.GetGuid() : null;
        }

        private static long? So(JsonElement p, string ten)
        {
            JsonElement v;
            long so;
            if (p.TryGetProperty(ten, out v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out so))
            {
                return so;
            }

            return null;
        }
    }
}
