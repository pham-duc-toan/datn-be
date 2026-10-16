using System;

namespace Crowd.Admin.Api.Entities
{
    /// <summary>
    /// Mot muc trong HANG DOI DUYET CHUNG (kien truc muc 5): read model do admin-svc dung tu event cua cac
    /// service chu. Moi service van giu bang trang thai cua loai don no so huu (business_profiles,
    /// identity_verifications, projects, withdrawals, annotations, disputes, links) va API duyet cua no;
    /// bang nay chi de admin co MOT man hinh: cai gi dang cho, cho tu bao gio, ai xin, bao nhieu tien.
    /// Khoa duy nhat (Kind, SubjectId): nop lai sau khi bi tu choi = mo lai cung dong.
    /// Ghi bang SQL trong HangDoiDuyet (khong qua EF) — entity nay chi de doc.
    /// </summary>
    public sealed class DonDuyet
    {
        private DonDuyet()
        {
            Kind = string.Empty;
            Status = string.Empty;
        }

        public Guid Id { get; private set; }

        /// <summary>kyb | identity | project | withdrawal | appeal | dispute | link</summary>
        public string Kind { get; private set; }

        /// <summary>Id ban ghi o service chu (businessId, userId, projectId, withdrawalId, annotationId, disputeId, linkId).</summary>
        public Guid SubjectId { get; private set; }

        /// <summary>waiting (du an cho ky quy) | pending | approved | rejected | cancelled | resolved</summary>
        public string Status { get; private set; }

        public string? Title { get; private set; }

        public Guid? RequesterId { get; private set; }

        public long? AmountVnd { get; private set; }

        /// <summary>Ly do tu choi / ket qua phan xu / ghi chu.</summary>
        public string? Note { get; private set; }

        public DateTimeOffset OpenedAt { get; private set; }

        public DateTimeOffset? DecidedAt { get; private set; }

        public Guid? DecidedBy { get; private set; }

        /// <summary>Thoi diem event moi nhat da ap dung — event den tre (cu hon) khong ghi de.</summary>
        public DateTimeOffset LastEventAt { get; private set; }
    }
}
