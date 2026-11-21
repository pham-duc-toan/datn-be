using System;
using System.Collections.Generic;

namespace Crowd.Reco.Domain
{
    /// <summary>
    /// Mot du an trong bo nho cua reco-svc (nap tu view v_reco_projects cua project-svc moi 30 giay + event mo / dong).
    /// So bai nop / dang lam / da bat dau CHI dung de cham diem trong server — KHONG BAO GIO tra cho labeler
    /// (labeler chi thay so suat, tien thuong, han chot — kien truc 3.38).
    /// </summary>
    public sealed class DuAnGoiY
    {
        public required Guid Id { get; init; }

        public required Guid OwnerId { get; init; }

        public required string Name { get; init; }

        public required string Description { get; init; }

        public required string Modality { get; init; }

        public required IReadOnlyList<string> Domains { get; init; }

        public required IReadOnlyList<string> Languages { get; init; }

        public required IReadOnlyList<string> ToolKinds { get; init; }

        public required long RewardVnd { get; init; }

        public required int Slots { get; init; }

        public required int QuestionCount { get; init; }

        public DateTimeOffset? Deadline { get; init; }

        public required DateTimeOffset PublishedAt { get; init; }

        public required bool IsPrivate { get; init; }

        public int? MinLevel { get; init; }

        public int? MinReputation { get; init; }

        public required bool RequireEntranceTest { get; init; }

        public required bool RequireVerifiedIdentity { get; init; }

        public required bool SensitiveContent { get; init; }

        public required bool NdaRequired { get; init; }

        /// <summary>Dang nhan bai: Running, kenh chuyen nghiep, dot thu bai chua dong, chua qua han chot.</summary>
        public required bool Accepting { get; init; }

        public required int SubmittedCount { get; init; }

        public required int InProgressCount { get; init; }

        /// <summary>Tong so lan bat dau lam (ke ca bo do / het gio) — tu so cho ti le "thay → bat dau".</summary>
        public required int StartedCount { get; init; }

        /// <summary>Trung vi giay moi cau tren bai da nop. null = chua co bai nop.</summary>
        public double? MedianSecondsPerQuestion { get; init; }

        /// <summary>Cong cu cua tap nhan (ten, loai, cau hoi, lop) — the cho hien TOAN BO cau hoi + cong cu.</summary>
        public required IReadOnlyList<CongCuGoiY> Tools { get; init; }

        /// <summary>So labeler (uoc luong HyperLogLog) da THAT SU thay the du an nay tren man hinh.</summary>
        public long SeenCount { get; init; }

        /// <summary>Luc reco-svc thay du an DANG NHAN BAI lan dau — de biet du an "mo sau luc tao danh sach" cua mot luot luot.</summary>
        public required DateTimeOffset OpenedAt { get; init; }

        /// <summary>Doi thu: bai da nop + dang lam.</summary>
        public int DoiThu
        {
            get { return SubmittedCount + InProgressCount; }
        }

        /// <summary>Moi the cua du an theo dang "loai|ma" — dung de tra bo dem so thich.</summary>
        public IEnumerable<KeyValuePair<string, string>> CacThe()
        {
            yield return new KeyValuePair<string, string>(LoaiThe.LoaiDuLieu, Modality);
            foreach (string d in Domains)
            {
                yield return new KeyValuePair<string, string>(LoaiThe.LinhVuc, d);
            }

            foreach (string l in Languages)
            {
                yield return new KeyValuePair<string, string>(LoaiThe.NgonNgu, l);
            }

            foreach (string t in ToolKinds)
            {
                yield return new KeyValuePair<string, string>(LoaiThe.CongCu, t);
            }
        }
    }

    /// <summary>Mot cong cu cua tap nhan tren the cho du an (labeler thay truoc khi mo du an).</summary>
    public sealed class CongCuGoiY
    {
        public required string Name { get; init; }

        public required string Kind { get; init; }

        /// <summary>Cau hoi cua cong cu (bat buoc truoc publish — kien truc 3.35).</summary>
        public string? Question { get; init; }

        /// <summary>Cac lop / lua chon (phan loai, khung, vung...). Rong = cong cu khong co lop.</summary>
        public required IReadOnlyList<string> Classes { get; init; }
    }

    /// <summary>Loai the trong bo dem so thich (tag_kind cua labeler_affinity).</summary>
    public static class LoaiThe
    {
        public const string LinhVuc = "domain";
        public const string NgonNgu = "language";
        public const string LoaiDuLieu = "modality";
        public const string CongCu = "tool";

        /// <summary>Bo dem DAT / TRUOT theo loai cong cu — nguon P(dat) khi chua co mo hinh hoc may.</summary>
        public const string DatTheoCongCu = "pass_tool";
    }
}
