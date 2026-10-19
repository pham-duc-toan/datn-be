using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Admin.Api.Dtos;
using Crowd.Admin.Api.Entities;
using Crowd.Admin.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Admin.Api.Services
{
    public enum KieuThayDoi
    {
        /// <summary>Mo (hoac mo lai) mot don cho duyet.</summary>
        Mo,

        /// <summary>Du an xin publish, dang cho ky quy — chua hien trong hang doi.</summary>
        ChoKyQuy,

        /// <summary>Ky quy xong: du an chuyen tu cho ky quy sang cho duyet.</summary>
        MoTuCho,

        /// <summary>Don da co ket qua.</summary>
        Dong,
    }

    /// <summary>Mot thay doi tren hang doi duyet suy ra tu MOT event (ham thuan PhanLoai).</summary>
    public sealed class ThayDoiDon
    {
        public required string Kind { get; init; }

        public required Guid SubjectId { get; init; }

        public required KieuThayDoi Kieu { get; init; }

        /// <summary>Trang thai moi khi Kieu = Dong.</summary>
        public string? Status { get; init; }

        public string? Title { get; init; }

        public Guid? RequesterId { get; init; }

        public long? AmountVnd { get; init; }

        public string? Note { get; init; }

        /// <summary>
        /// Dong ma chua thay dong mo (event den nguoc thu tu) thi van ghi dong da dong — de event mo den tre
        /// (cu hon) khong mo lai. Chi bat cho event CHI xay ra voi don (khong bat cho payout.requested,
        /// project.published... vi chung xay ra ca khi khong co don nao).
        /// </summary>
        public bool TaoNeuChuaCo { get; init; }
    }

    /// <summary>
    /// Hang doi duyet chung (kien truc muc 5): chieu event cua 7 luong duyet vao bang approval_requests.
    /// AuditConsumer goi ApDungAsync trong CUNG transaction ghi audit, chi khi event lan dau thay (khong lam
    /// hai lan khi bus giao lai). Bang luon dung lai duoc tu audit_events (DungLaiAsync).
    ///
    ///   kyb        business.kyb_submitted → business.verified / business.kyb_rejected
    ///   identity   user.identity_verification_submitted → user.identity_verified / user.identity_rejected
    ///   project    project.publish_requested (cho ky quy) → escrow.reserved (cho duyet)
    ///              → project.published / project.cancelled (admin tu choi = rejected) / escrow.rejected
    ///   withdrawal withdrawal.review_requested → payout.requested / withdrawal.rejected
    ///   appeal     attempt.appeal_opened → attempt.appeal_resolved (khieu nai CA BAI, docs 3.18)
    ///   link       link.review_requested → link.disabled (admin = bao cao dung) / link.review_dismissed
    ///   referral   referral.flagged → referral.registered (chap nhan) / referral.rejected
    /// </summary>
    public sealed class HangDoiDuyet
    {
        public static readonly string[] LoaiDon = new string[] { "kyb", "identity", "project", "withdrawal", "appeal", "link", "referral" };

        /// <summary>Moi loai event lam thay doi hang doi — DungLaiAsync chi doc lai cac loai nay.</summary>
        public static readonly string[] LoaiEvent = new string[]
        {
            "business.kyb_submitted", "business.verified", "business.kyb_rejected",
            "user.identity_verification_submitted", "user.identity_verified", "user.identity_rejected",
            "project.publish_requested", "escrow.reserved", "escrow.rejected", "project.published", "project.cancelled",
            "withdrawal.review_requested", "payout.requested", "withdrawal.rejected",
            "attempt.appeal_opened", "attempt.appeal_resolved",
            "link.review_requested", "link.disabled", "link.review_dismissed",
            "referral.flagged", "referral.registered", "referral.rejected",
        };

        private readonly AdminDbContext _db;

        public HangDoiDuyet(AdminDbContext db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            _db = db;
        }

        // =====================================================================
        // Chieu event → thay doi (ham thuan)
        // =====================================================================

        public static ThayDoiDon? PhanLoai(string eventType, JsonElement p, string? actorRole)
        {
            bool laAdmin = string.Equals(actorRole, "admin", StringComparison.OrdinalIgnoreCase);
            switch (eventType)
            {
                case "business.kyb_submitted":
                    return Mo("kyb", Id(p, "businessId"), Chuoi(p, "companyName"), Id(p, "businessId"), null, null);
                case "business.verified":
                    return Dong("kyb", Id(p, "businessId"), "approved", null, true);
                case "business.kyb_rejected":
                    return Dong("kyb", Id(p, "businessId"), "rejected", Chuoi(p, "reason"), true);

                case "user.identity_verification_submitted":
                    return Mo("identity", Id(p, "userId"), Chuoi(p, "fullName"), Id(p, "userId"), null, null);
                case "user.identity_verified":
                    return Dong("identity", Id(p, "userId"), "approved", null, true);
                case "user.identity_rejected":
                    return Dong("identity", Id(p, "userId"), "rejected", Chuoi(p, "reason"), true);

                case "project.publish_requested":
                    return new ThayDoiDon
                    {
                        Kind = "project",
                        SubjectId = Id(p, "projectId"),
                        Kieu = KieuThayDoi.ChoKyQuy,
                        Title = Chuoi(p, "projectName"),
                        RequesterId = Id(p, "ownerId"),
                        AmountVnd = So(p, "escrowAmountVnd"),
                    };
                case "escrow.reserved":
                    return new ThayDoiDon { Kind = "project", SubjectId = Id(p, "projectId"), Kieu = KieuThayDoi.MoTuCho };
                case "escrow.rejected":
                    return Dong("project", Id(p, "projectId"), "cancelled", Chuoi(p, "reason"), false);
                case "project.published":
                    return Dong("project", Id(p, "projectId"), "approved", null, false);
                case "project.cancelled":
                    return Dong("project", Id(p, "projectId"), laAdmin ? "rejected" : "cancelled", Chuoi(p, "reason"), false);

                case "withdrawal.review_requested":
                    return Mo("withdrawal", Id(p, "withdrawalId"), null, Id(p, "labelerId"), So(p, "amountVnd"), null);
                case "payout.requested":
                    return Dong("withdrawal", Id(p, "withdrawalId"), "approved", null, false);
                case "withdrawal.rejected":
                    return Dong("withdrawal", Id(p, "withdrawalId"), "rejected", Chuoi(p, "reason"), true);

                case "attempt.appeal_opened":
                    return Mo("appeal", Id(p, "attemptId"), Chuoi(p, "message"), Id(p, "labelerId"), null,
                        "giong da so " + So(p, "agreementPercent") + "% / nguong " + So(p, "passThresholdPercent") + "%");
                case "attempt.appeal_resolved":
                    return Dong("appeal", Id(p, "attemptId"), p.GetProperty("accepted").GetBoolean() ? "approved" : "rejected", Chuoi(p, "note"), true);

                case "link.review_requested":
                    return Mo("link", Id(p, "linkId"), Chuoi(p, "code"), Id(p, "ownerId"), null, So(p, "reportCount") + " bao cao");
                case "link.disabled":
                    // Admin vo hieu hoa = bao cao dung; chu link tu xoa / het han thi don huy theo.
                    return Dong("link", Id(p, "linkId"), laAdmin ? "approved" : "cancelled", Chuoi(p, "reason"), false);
                case "link.review_dismissed":
                    return Dong("link", Id(p, "linkId"), "rejected", "bo qua bao cao", true);

                case "referral.flagged":
                    return Mo("referral", Id(p, "referredId"), Chuoi(p, "reasons"), Id(p, "referrerId"), null, null);
                case "referral.registered":
                    // Gioi thieu binh thuong cung phat event nay → chi dong don dang co.
                    return Dong("referral", Id(p, "referredId"), "approved", null, false);
                case "referral.rejected":
                    return Dong("referral", Id(p, "referredId"), "rejected", Chuoi(p, "reason"), true);

                default:
                    return null;
            }
        }

        private static ThayDoiDon Mo(string kind, Guid subject, string? title, Guid? requester, long? amount, string? note)
        {
            return new ThayDoiDon
            {
                Kind = kind,
                SubjectId = subject,
                Kieu = KieuThayDoi.Mo,
                Title = title,
                RequesterId = requester,
                AmountVnd = amount,
                Note = note,
            };
        }

        private static ThayDoiDon Dong(string kind, Guid subject, string status, string? note, bool taoNeuChuaCo)
        {
            return new ThayDoiDon
            {
                Kind = kind,
                SubjectId = subject,
                Kieu = KieuThayDoi.Dong,
                Status = status,
                Note = note,
                TaoNeuChuaCo = taoNeuChuaCo,
            };
        }

        private static Guid Id(JsonElement p, string ten)
        {
            return p.GetProperty(ten).GetGuid();
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

        // =====================================================================
        // Ghi (trong transaction cua noi goi)
        // =====================================================================

        /// <summary>Ap dung MOT event. Event khong lien quan → khong lam gi. Tra true neu co thay doi.</summary>
        public static async Task<bool> ApDungAsync(
            AdminDbContext db, string eventType, string payloadJson, DateTimeOffset luc, Guid? actorId, string? actorRole, CancellationToken ct)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (Array.IndexOf(LoaiEvent, eventType) < 0)
            {
                return false;
            }

            ThayDoiDon? t;
            using (JsonDocument doc = JsonDocument.Parse(payloadJson))
            {
                t = PhanLoai(eventType, doc.RootElement, actorRole);
            }

            if (t == null)
            {
                return false;
            }

            string? tieuDe = t.Title != null && t.Title.Length > 300 ? t.Title.Substring(0, 300) : t.Title;
            string? ghiChu = t.Note != null && t.Note.Length > 1000 ? t.Note.Substring(0, 1000) : t.Note;
            switch (t.Kieu)
            {
                case KieuThayDoi.Mo:
                case KieuThayDoi.ChoKyQuy:
                    string trangThai = t.Kieu == KieuThayDoi.Mo ? "pending" : "waiting";
                    return await db.Database.ExecuteSqlInterpolatedAsync($@"
                        INSERT INTO approval_requests (id, kind, subject_id, status, title, requester_id, amount_vnd, note, opened_at, last_event_at)
                        VALUES ({Guid.CreateVersion7()}, {t.Kind}, {t.SubjectId}, {trangThai}, {tieuDe}, {t.RequesterId}, {t.AmountVnd}, {ghiChu}, {luc}, {luc})
                        ON CONFLICT (kind, subject_id) DO UPDATE SET
                            status = EXCLUDED.status,
                            title = COALESCE(EXCLUDED.title, approval_requests.title),
                            requester_id = COALESCE(EXCLUDED.requester_id, approval_requests.requester_id),
                            amount_vnd = COALESCE(EXCLUDED.amount_vnd, approval_requests.amount_vnd),
                            note = EXCLUDED.note,
                            opened_at = EXCLUDED.opened_at,
                            decided_at = NULL,
                            decided_by = NULL,
                            last_event_at = EXCLUDED.last_event_at
                        WHERE approval_requests.last_event_at <= EXCLUDED.last_event_at", ct) > 0;

                case KieuThayDoi.MoTuCho:
                    return await db.Database.ExecuteSqlInterpolatedAsync($@"
                        UPDATE approval_requests SET status = 'pending', opened_at = {luc}, last_event_at = {luc}
                        WHERE kind = {t.Kind} AND subject_id = {t.SubjectId} AND status = 'waiting' AND last_event_at <= {luc}", ct) > 0;

                default:
                    int sua = await db.Database.ExecuteSqlInterpolatedAsync($@"
                        UPDATE approval_requests
                        SET status = {t.Status}, note = COALESCE({ghiChu}, note), decided_at = {luc}, decided_by = {actorId}, last_event_at = {luc}
                        WHERE kind = {t.Kind} AND subject_id = {t.SubjectId} AND status IN ('pending', 'waiting') AND last_event_at <= {luc}", ct);
                    if (sua > 0 || !t.TaoNeuChuaCo)
                    {
                        return sua > 0;
                    }

                    // Dong den truoc mo (khac producer, khac queue): ghi san dong da dong.
                    return await db.Database.ExecuteSqlInterpolatedAsync($@"
                        INSERT INTO approval_requests (id, kind, subject_id, status, note, opened_at, decided_at, decided_by, last_event_at)
                        VALUES ({Guid.CreateVersion7()}, {t.Kind}, {t.SubjectId}, {t.Status}, {ghiChu}, {luc}, {luc}, {actorId}, {luc})
                        ON CONFLICT (kind, subject_id) DO UPDATE SET
                            status = EXCLUDED.status, note = COALESCE(EXCLUDED.note, approval_requests.note),
                            decided_at = EXCLUDED.decided_at, decided_by = EXCLUDED.decided_by, last_event_at = EXCLUDED.last_event_at
                        WHERE approval_requests.last_event_at < EXCLUDED.last_event_at", ct) > 0;
            }
        }

        /// <summary>
        /// Dung lai toan bo hang doi tu audit_events (vd lan dau trien khai, hoac sau khi sua cach chieu).
        /// Event cu hon admin.audit_retention_days da bi xoa thi khong con — don cu nhu vay coi nhu da xong.
        /// </summary>
        public async Task<int> DungLaiAsync(CancellationToken ct)
        {
            int so = 0;
            var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                await _db.Database.ExecuteSqlRawAsync("DELETE FROM approval_requests", ct);
                List<AuditEvent> ds = await _db.AuditEvents.AsNoTracking()
                    .Where(e => LoaiEvent.Contains(e.EventType))
                    .OrderBy(e => e.OccurredAt).ThenBy(e => e.EventId)
                    .ToListAsync(ct);
                foreach (AuditEvent e in ds)
                {
                    if (await ApDungAsync(_db, e.EventType, e.PayloadJson, e.OccurredAt, e.ActorId, e.ActorRole, ct))
                    {
                        so++;
                    }
                }

                await tx.CommitAsync(ct);
            }
            finally
            {
                await tx.DisposeAsync();
            }

            return so;
        }

        // =====================================================================
        // Doc
        // =====================================================================

        /// <summary>status: pending (mac dinh) | waiting | approved | rejected | cancelled | resolved | all.</summary>
        public async Task<PagedResponse<ApprovalItemResponse>> DanhSachAsync(string? kind, string? status, int page, int pageSize, CancellationToken ct)
        {
            int trang = page < 1 ? 1 : page;
            int co = pageSize < 1 || pageSize > 200 ? 50 : pageSize;
            string tt = string.IsNullOrWhiteSpace(status) ? "pending" : status.Trim().ToLowerInvariant();

            IQueryable<DonDuyet> q = _db.DonDuyets.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(kind))
            {
                string k = kind.Trim().ToLowerInvariant();
                if (Array.IndexOf(LoaiDon, k) < 0)
                {
                    throw new AdminException(400, "loai_don_khong_hop_le", "kind: " + string.Join(" | ", LoaiDon) + ".");
                }

                q = q.Where(d => d.Kind == k);
            }

            if (tt != "all")
            {
                q = q.Where(d => d.Status == tt);
            }

            // Moi nhat truoc (mac dinh moi danh sach). Cho duyet: theo luc mo don; con lai: theo su kien cuoi.
            q = tt == "pending" || tt == "waiting"
                ? q.OrderByDescending(d => d.OpenedAt).ThenByDescending(d => d.Id)
                : q.OrderByDescending(d => d.LastEventAt).ThenByDescending(d => d.Id);

            int tong = await q.CountAsync(ct);
            List<DonDuyet> ds = await q.Skip((trang - 1) * co).Take(co).ToListAsync(ct);
            return new PagedResponse<ApprovalItemResponse>
            {
                Items = ds.Select(TaoResponse).ToList(),
                Page = trang,
                PageSize = co,
                Total = tong,
            };
        }

        public async Task<ApprovalSummaryResponse> TomTatAsync(DateTimeOffset bayGio, CancellationToken ct)
        {
            var nhom = await _db.DonDuyets.AsNoTracking()
                .Where(d => d.Status == "pending")
                .GroupBy(d => d.Kind)
                .Select(g => new { Kind = g.Key, SoLuong = g.Count(), CuNhat = g.Min(d => d.OpenedAt) })
                .ToListAsync(ct);

            List<ApprovalKindSummary> tung = new List<ApprovalKindSummary>();
            foreach (string k in LoaiDon)
            {
                var n = nhom.FirstOrDefault(x => x.Kind == k);
                tung.Add(new ApprovalKindSummary
                {
                    Kind = k,
                    Pending = n == null ? 0 : n.SoLuong,
                    OldestOpenedAt = n == null ? null : n.CuNhat,
                    OldestWaitingHours = n == null ? null : Math.Round((bayGio - n.CuNhat).TotalHours, 1),
                });
            }

            return new ApprovalSummaryResponse { TotalPending = tung.Sum(x => x.Pending), Kinds = tung };
        }

        private static ApprovalItemResponse TaoResponse(DonDuyet d)
        {
            return new ApprovalItemResponse
            {
                Id = d.Id,
                Kind = d.Kind,
                SubjectId = d.SubjectId,
                Status = d.Status,
                Title = d.Title,
                RequesterId = d.RequesterId,
                AmountVnd = d.AmountVnd,
                Note = d.Note,
                OpenedAt = d.OpenedAt,
                DecidedAt = d.DecidedAt,
                DecidedBy = d.DecidedBy,
            };
        }
    }
}
