using System;

namespace Crowd.Admin.Api.Entities
{
    /// <summary>
    /// Mot dong nhat ky audit (FP-06, VD-S-10): ban sao CHI GHI THEM cua moi event di qua bus — ai
    /// (actor trong envelope) lam gi (eventType + payload), luc nao, thuoc chuoi thao tac nao
    /// (correlationId). Khoa chinh = eventId nen nhan trung khong ghi hai lan.
    /// Khong co API sua / xoa; chi job don theo admin.audit_retention_days.
    /// </summary>
    public sealed class AuditEvent
    {
        private AuditEvent()
        {
            EventType = string.Empty;
            Producer = string.Empty;
            PayloadJson = string.Empty;
        }

        public Guid EventId { get; private set; }

        public string EventType { get; private set; }

        public string Producer { get; private set; }

        public DateTimeOffset OccurredAt { get; private set; }

        public Guid CorrelationId { get; private set; }

        public Guid? CausationId { get; private set; }

        /// <summary>Nguoi lam; null = he thong (consumer, worker).</summary>
        public Guid? ActorId { get; private set; }

        public string? ActorRole { get; private set; }

        public string PayloadJson { get; private set; }

        public DateTimeOffset ReceivedAt { get; private set; }

        public static AuditEvent Tao(
            Guid eventId, string eventType, string producer, DateTimeOffset occurredAt, Guid correlationId, Guid? causationId,
            Guid? actorId, string? actorRole, string payloadJson, DateTimeOffset receivedAt)
        {
            AuditEvent e = new AuditEvent();
            e.EventId = eventId;
            e.EventType = eventType;
            e.Producer = producer;
            e.OccurredAt = occurredAt;
            e.CorrelationId = correlationId;
            e.CausationId = causationId;
            e.ActorId = actorId;
            e.ActorRole = actorRole;
            e.PayloadJson = payloadJson;
            e.ReceivedAt = receivedAt;
            return e;
        }
    }

    /// <summary>Thong bao toan he thong admin da gui (FM-09).</summary>
    public sealed class Announcement
    {
        public const int DoDaiTieuDeToiDa = 200;
        public const int DoDaiNoiDungToiDa = 2000;

        private Announcement()
        {
            Title = string.Empty;
            Body = string.Empty;
            Audience = string.Empty;
        }

        public Guid Id { get; private set; }

        public string Title { get; private set; }

        public string Body { get; private set; }

        /// <summary>all | business | labeler | sharer</summary>
        public string Audience { get; private set; }

        public bool SendEmail { get; private set; }

        public Guid CreatedBy { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public static Announcement Tao(string title, string body, string audience, bool sendEmail, Guid createdBy, DateTimeOffset luc)
        {
            Announcement a = new Announcement();
            a.Id = Guid.CreateVersion7();
            a.Title = title;
            a.Body = body;
            a.Audience = audience;
            a.SendEmail = sendEmail;
            a.CreatedBy = createdBy;
            a.CreatedAt = luc;
            return a;
        }
    }
}
