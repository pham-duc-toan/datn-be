using System;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.Projects;

namespace Crowd.Project.Domain.Collections
{
    /// <summary>
    /// DOT THU BAI cua mot du an da duyet (bang project_collections, 1-1 voi projects) — phan trang thai CHI
    /// co tu luc labeler bat dau lam bai: tran nguoi lam, ly do dong thu bai, moc cham / khieu nai / chot.
    /// Cau hinh (tap nhan, thuong, so suat, han chot...) KHONG chep vao day — doc thang tu LabelingProject.
    ///
    /// VI SAO TACH KHOI projects: bat dau / nop bai DEM SUAT duoi khoa dong. Khoa dong nay (FOR UPDATE) thay
    /// vi khoa dong projects: luong labeler (hang tram lan bam / phut) khong xep hang chung voi thao tac cua
    /// chu du an, va dong thu bai khong lam doi xmin cua projects (chu du an khong gap 409 vo co).
    ///
    /// THU TU KHOA: project_collections → attempts → samples. Moi duong deu theo thu tu nay (khong deadlock).
    /// </summary>
    public sealed class CollectionRound
    {
        private CollectionRound()
        {
        }

        public Guid ProjectId { get; private set; }

        /// <summary>
        /// Tran so nguoi lam bai (dang lam + da nop) = Slots x project.participant_multiplier, CHUP luc duyet
        /// (admin doi setting giua chung khong lam lech du an dang chay). Du tran thi dong thu bai.
        /// </summary>
        public int MaxParticipants { get; private set; }

        /// <summary>Luc tam dung gan nhat (de cong bu thoi gian giu bai khi chay tiep). null = khong tam dung.</summary>
        public DateTimeOffset? PausedAt { get; private set; }

        /// <summary>du_suat | het_han | ket_thuc_som. null = con nhan bai. Luc dong = projects.grading_started_at.</summary>
        public string? FinishReason { get; private set; }

        /// <summary>Luc quality-svc cham xong (project.graded).</summary>
        public DateTimeOffset? GradedAt { get; private set; }

        /// <summary>Han khieu nai = luc cham + task.appeal_window.</summary>
        public DateTimeOffset? AppealDeadline { get; private set; }

        /// <summary>Luc chot so bai duoc thuong (het han khieu nai, khong con khieu nai mo) → du an Hoan thanh.</summary>
        public DateTimeOffset? SettledAt { get; private set; }

        /// <summary>So bai DUOC THUONG luc chot — ledger tra du so nay roi hoan phan ky quy con lai.</summary>
        public int? PassedCount { get; private set; }

        /// <summary>
        /// Tien do CUOI CUNG (JSON cua ProgressResponse), chup TRUOC khi don du lieu van hanh cua du an da dong.
        /// Co gia tri = da bat dau don; tu do API tien do tra ban chup nay.
        /// </summary>
        public string? FinalProgressJson { get; private set; }

        /// <summary>Luc don XONG du lieu van hanh. null = chua don (hoac dang don do, lan sau don tiep).</summary>
        public DateTimeOffset? DataPurgedAt { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        /// <summary>Admin duyet du an (Dang chay): mo dot thu bai.</summary>
        public static CollectionRound Mo(Guid projectId, int slots, int heSoNguoiLam, DateTimeOffset luc)
        {
            if (slots < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(slots));
            }

            CollectionRound c = new CollectionRound();
            c.ProjectId = projectId;
            c.MaxParticipants = Math.Max(slots, slots * Math.Max(1, heSoNguoiLam));
            c.CreatedAt = luc;
            return c;
        }

        public void GhiTamDung(DateTimeOffset luc)
        {
            PausedAt = luc;
        }

        /// <summary>Chay tiep: tra ve thoi gian DA tam dung (cong bu cho bai dang lam), roi xoa moc.</summary>
        public TimeSpan TiepTuc(DateTimeOffset luc)
        {
            DateTimeOffset? tu = PausedAt;
            PausedAt = null;
            if (!tu.HasValue || luc <= tu.Value)
            {
                return TimeSpan.Zero;
            }

            return luc - tu.Value;
        }

        public bool DaDongThuBai
        {
            get { return FinishReason != null; }
        }

        /// <summary>Dong thu bai (mot lan). Tra ve false neu da dong roi.</summary>
        public bool DongThuBai(string lyDo)
        {
            if (string.IsNullOrWhiteSpace(lyDo))
            {
                throw new ArgumentException("Thieu ly do.", nameof(lyDo));
            }

            if (FinishReason != null)
            {
                return false;
            }

            FinishReason = lyDo;
            return true;
        }

        /// <summary>project.graded: mo han khieu nai. Giao lai → giu moc cu.</summary>
        public bool GhiDaCham(DateTimeOffset luc, TimeSpan hanKhieuNai)
        {
            if (GradedAt.HasValue)
            {
                return false;
            }

            GradedAt = luc;
            AppealDeadline = luc + hanKhieuNai;
            return true;
        }

        /// <summary>Chot so bai duoc thuong (het han khieu nai, khong con khieu nai mo). Mot lan.</summary>
        public bool Chot(int soNguoiDat, DateTimeOffset luc)
        {
            if (SettledAt.HasValue || !GradedAt.HasValue)
            {
                return false;
            }

            SettledAt = luc;
            PassedCount = soNguoiDat;
            return true;
        }

        /// <summary>Chup tien do cuoi — chi du an DA DONG, va chi mot lan (lan sau giu ban dau).</summary>
        public void ChupTienDoCuoi(LabelingProject duAn, string tienDoJson)
        {
            if (duAn == null)
            {
                throw new ArgumentNullException(nameof(duAn));
            }

            if (string.IsNullOrWhiteSpace(tienDoJson))
            {
                throw new ArgumentException("Thieu tien do.", nameof(tienDoJson));
            }

            if (duAn.Status != ProjectStatus.Completed && duAn.Status != ProjectStatus.Cancelled)
            {
                throw new RuleViolationException("du_an_chua_dong", "Chỉ dọn dữ liệu vận hành của dự án đã đóng.");
            }

            if (FinalProgressJson == null)
            {
                FinalProgressJson = tienDoJson;
            }
        }

        public void DanhDauDaDon(DateTimeOffset luc)
        {
            if (FinalProgressJson == null)
            {
                throw new InvalidOperationException("Phai chup tien do cuoi truoc khi danh dau da don.");
            }

            if (DataPurgedAt == null)
            {
                DataPurgedAt = luc;
            }
        }

        /// <summary>
        /// Con nhan bai (bat dau / nop) khong: du an dang chay, chua dong thu bai, chua qua han chot. Doc tu
        /// chinh projects (khong con ban sao trang thai).
        /// </summary>
        public static bool ConNhanBai(LabelingProject duAn, CollectionRound? dot, DateTimeOffset luc)
        {
            if (duAn == null)
            {
                throw new ArgumentNullException(nameof(duAn));
            }

            return dot != null
                   && duAn.Status == ProjectStatus.Running
                   && !dot.DaDongThuBai
                   && (!duAn.Deadline.HasValue || luc < duAn.Deadline.Value);
        }

        /// <summary>open | finished | graded | settled — giai doan hien cho labeler va chu du an.</summary>
        public string GiaiDoan()
        {
            if (SettledAt.HasValue)
            {
                return "settled";
            }

            if (GradedAt.HasValue)
            {
                return "graded";
            }

            if (DaDongThuBai)
            {
                return "finished";
            }

            return "open";
        }
    }
}
