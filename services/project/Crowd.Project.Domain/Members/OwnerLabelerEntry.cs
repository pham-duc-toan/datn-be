using System;
using Crowd.Project.Domain.Common;

namespace Crowd.Project.Domain.Members
{
    public enum OwnerListKind
    {
        /// <summary>Uu tien: moi nhanh vao du an moi, don xin tham gia tu duyet.</summary>
        Preferred,

        /// <summary>Chan: khong vao duoc MOI du an cua doanh nghiep; dang o du an nao thi bi chan o do.</summary>
        Blocked,
    }

    /// <summary>
    /// Danh sach UU TIEN / CHAN cua doanh nghiep doi voi labeler (FB-23) — ap cho MOI du an cua doanh
    /// nghiep (chan rieng mot du an da co o project_members). Moi cap (doanh nghiep, labeler) mot dong:
    /// mot nguoi hoac uu tien hoac bi chan, khong ca hai.
    /// </summary>
    public sealed class OwnerLabelerEntry
    {
        public const int DoDaiGhiChuToiDa = 500;

        private OwnerLabelerEntry()
        {
        }

        public Guid OwnerId { get; private set; }

        public Guid LabelerId { get; private set; }

        public OwnerListKind Kind { get; private set; }

        public string? Note { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public DateTimeOffset UpdatedAt { get; private set; }

        public static OwnerLabelerEntry Tao(Guid ownerId, Guid labelerId, OwnerListKind kind, string? note, DateTimeOffset luc)
        {
            if (ownerId == Guid.Empty || labelerId == Guid.Empty)
            {
                throw new InvalidValueException("id_rong", "Thieu doanh nghiep hoac labeler.");
            }

            if (ownerId == labelerId)
            {
                throw new InvalidValueException("khong_tu_them", "Khong them chinh minh vao danh sach.");
            }

            OwnerLabelerEntry e = new OwnerLabelerEntry();
            e.OwnerId = ownerId;
            e.LabelerId = labelerId;
            e.CreatedAt = luc;
            e.DoiLoai(kind, note, luc);
            return e;
        }

        public void DoiLoai(OwnerListKind kind, string? note, DateTimeOffset luc)
        {
            string? g = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            if (g != null && g.Length > DoDaiGhiChuToiDa)
            {
                throw new InvalidValueException("ghi_chu_qua_dai", "Ghi chu toi da " + DoDaiGhiChuToiDa + " ky tu.");
            }

            Kind = kind;
            Note = g;
            UpdatedAt = luc;
        }
    }

    /// <summary>
    /// Doanh nghiep danh gia labeler da lam trong MOT du an cua minh (FB-23), 1–5 sao + nhan xet. Moi
    /// (du an, labeler) mot danh gia, sua lai duoc. Diem trung binh hien o ho so labeler cho doanh
    /// nghiep khac tham khao.
    /// </summary>
    public sealed class LabelerRating
    {
        public const int DoDaiNhanXetToiDa = 1000;

        private LabelerRating()
        {
        }

        public Guid ProjectId { get; private set; }

        public Guid LabelerId { get; private set; }

        public Guid OwnerId { get; private set; }

        public int Stars { get; private set; }

        public string? Comment { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public DateTimeOffset UpdatedAt { get; private set; }

        public static LabelerRating Tao(Guid projectId, Guid labelerId, Guid ownerId, int stars, string? comment, DateTimeOffset luc)
        {
            LabelerRating r = new LabelerRating();
            r.ProjectId = projectId;
            r.LabelerId = labelerId;
            r.OwnerId = ownerId;
            r.CreatedAt = luc;
            r.Sua(stars, comment, luc);
            return r;
        }

        public void Sua(int stars, string? comment, DateTimeOffset luc)
        {
            if (stars < 1 || stars > 5)
            {
                throw new InvalidValueException("so_sao_khong_hop_le", "Danh gia tu 1 den 5 sao.");
            }

            string? c = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
            if (c != null && c.Length > DoDaiNhanXetToiDa)
            {
                throw new InvalidValueException("nhan_xet_qua_dai", "Nhan xet toi da " + DoDaiNhanXetToiDa + " ky tu.");
            }

            Stars = stars;
            Comment = c;
            UpdatedAt = luc;
        }
    }
}
