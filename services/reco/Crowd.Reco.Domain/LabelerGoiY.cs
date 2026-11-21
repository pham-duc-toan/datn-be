using System;
using System.Collections.Generic;

namespace Crowd.Reco.Domain
{
    /// <summary>
    /// Nhung gi bo goi y biet ve MOT labeler luc xep feed. Cap do / uy tin / xac minh / du tuoi lay tu CLAIM cua access
    /// token (khong goi identity moi request); so thich va tap loai tru lay tu Redis (mot lan doc).
    /// Day chi la LOC GOI Y — project-svc van tu kiem dieu kien that khi bat dau lam bai.
    /// </summary>
    public sealed class LabelerGoiY
    {
        public required Guid UserId { get; init; }

        public required int Level { get; init; }

        public int? Reputation { get; init; }

        public required bool IdentityVerified { get; init; }

        public required bool Adult { get; init; }

        public required SoThich SoThich { get; init; }

        /// <summary>Du an da nop / bi chan / bam "khong quan tam" — khong bao gio goi y.</summary>
        public required IReadOnlySet<Guid> LoaiTru { get; init; }

        /// <summary>Du an RIENG TU nguoi nay la thanh vien dang hoat dong — duoc thay.</summary>
        public required IReadOnlySet<Guid> ThanhVien { get; init; }

        /// <summary>Doanh nghiep da chan nguoi nay (danh sach chan cap doanh nghiep).</summary>
        public required IReadOnlySet<Guid> DoanhNghiepChan { get; init; }
    }

    /// <summary>Loc "labeler nay lam duoc du an nay ngay khong" — CUNG luat voi chuong trinh cho du an cua project-svc.</summary>
    public static class DieuKienGoiY
    {
        public static bool LamDuoc(DuAnGoiY duAn, LabelerGoiY nguoi)
        {
            if (duAn == null)
            {
                throw new ArgumentNullException(nameof(duAn));
            }

            if (nguoi == null)
            {
                throw new ArgumentNullException(nameof(nguoi));
            }

            if (!duAn.Accepting)
            {
                return false;
            }

            if (nguoi.LoaiTru.Contains(duAn.Id) || nguoi.DoanhNghiepChan.Contains(duAn.OwnerId))
            {
                return false;
            }

            if (duAn.OwnerId == nguoi.UserId)
            {
                return false;
            }

            if (duAn.IsPrivate && !nguoi.ThanhVien.Contains(duAn.Id))
            {
                return false;
            }

            if (duAn.MinLevel.HasValue && nguoi.Level < duAn.MinLevel.Value)
            {
                return false;
            }

            if (duAn.MinReputation.HasValue && (!nguoi.Reputation.HasValue || nguoi.Reputation.Value < duAn.MinReputation.Value))
            {
                return false;
            }

            if ((duAn.RequireVerifiedIdentity || duAn.SensitiveContent) && !nguoi.IdentityVerified)
            {
                return false;
            }

            if (duAn.SensitiveContent && !nguoi.Adult)
            {
                return false;
            }

            return true;
        }
    }
}
