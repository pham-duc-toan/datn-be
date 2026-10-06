using System;
using System.Collections.Generic;
using Crowd.Labeling;
using Crowd.Project.Domain.Common;

namespace Crowd.Project.Domain.Projects
{
    /// <summary>
    /// AGGREGATE ROOT: mot du an gan nhan.
    ///
    /// Moi thay doi deu di qua mot phuong thuc co ten nghiep vu (YeuCauPublish,
    /// TamDung...) — khong co setter public nao. Phuong thuc tu kiem luat roi
    /// moi doi trang thai, nen KHONG THE dua du an vao trang thai sai du goi tu
    /// controller, consumer hay worker nao.
    ///
    /// Ten la LabelingProject chu khong phai Project: trong namespace
    /// Crowd.Project.*, chu "Project" bi hieu la namespace Crowd.Project truoc
    /// khi toi kieu — cung loi da khien task-svc phai doi thanh Crowd.Tasking.
    ///
    /// Thanh vien, dataset, cau hoi vang KHONG nam trong aggregate nay: moi thu
    /// co the len toi hang nghin dong, nap het vao bo nho chi de them mot
    /// labeler la lang phi. Chung tham chieu toi du an bang ProjectId.
    /// </summary>
    public sealed class LabelingProject
    {
        public const int DoDaiTenToiDa = 200;
        public const int DoDaiMoTaToiDa = 5000;
        public const int RedundancyToiDa = 10;
        public const int SoCauTestToiDa = 50;

        /// <summary>EF Core dung constructor nay de dung lai doi tuong tu database.</summary>
        private LabelingProject()
        {
            Name = string.Empty;
            Description = string.Empty;
        }

        public Guid Id { get; private set; }

        /// <summary>Doanh nghiep tao du an. Cung duoc ghi thanh dong 'owner' trong project_members.</summary>
        public Guid OwnerId { get; private set; }

        public string Name { get; private set; }

        public string Description { get; private set; }

        public TaskType TaskType { get; private set; }

        /// <summary>
        /// Ten loai nhan cua du an trong dinh dang nhan chung (Crowd.Labeling) —
        /// nhan va dap an cua du an nay deu mang loai nay.
        /// </summary>
        public string LabelTaskType
        {
            get
            {
                switch (TaskType)
                {
                    case TaskType.ImageClassification:
                        return LabelTaskTypes.ImageClassification;
                    default:
                        throw new InvalidValueException(
                            "loai_bai_toan_chua_ho_tro",
                            "Chua co dinh dang nhan cho loai bai toan " + TaskType + ".");
                }
            }
        }

        public ProjectStatus Status { get; private set; }

        public ProjectVisibility Visibility { get; private set; }

        /// <summary>null = chua dinh nghia tap nhan.</summary>
        public LabelSchema? LabelSchema { get; private set; }

        /// <summary>null = chua viet huong dan.</summary>
        public Guideline? Guideline { get; private set; }

        // ---- Cau hinh gia (FB-14). Tien la long, so nguyen dong (VD-M-07). ----

        /// <summary>Thu lao cho MOT nhan. 0 = chua cau hinh.</summary>
        public long UnitPriceVnd { get; private set; }

        /// <summary>So nguoi gan cung mot mau. 0 = chua cau hinh.</summary>
        public int Redundancy { get; private set; }

        /// <summary>Ngan sach toi da — cung la so tien ky quy khi publish.</summary>
        public long BudgetVnd { get; private set; }

        public DateTimeOffset? Deadline { get; private set; }

        // ---- Kenh phan phoi (FB-17) ----

        public bool AllowProfessional { get; private set; }

        public bool AllowLinkGateway { get; private set; }

        public bool AllowCollaborative { get; private set; }

        // ---- Dieu kien tham gia (FB-15) ----

        /// <summary>
        /// null = khong gioi han. project-svc chi LUU va PHAT di; task-svc moi la
        /// noi loc theo cap do/uy tin, vi no giu ban sao labeler_cache.
        /// </summary>
        public int? MinLevel { get; private set; }

        /// <summary>Thang 0-100. null = khong gioi han.</summary>
        public int? MinReputation { get; private set; }

        public bool RequireEntranceTest { get; private set; }

        /// <summary>So cau moi lan lam bai test.</summary>
        public int EntranceQuestionCount { get; private set; }

        /// <summary>Phan tram dung toi thieu de dau, 1-100.</summary>
        public int EntrancePassPercent { get; private set; }

        // ---- Dau vet vong doi ----

        /// <summary>Ly do cua lan chuyen trang thai gan nhat (bi tu choi, bi huy...).</summary>
        public string? StatusReason { get; private set; }

        /// <summary>
        /// Phan tram phi nen tang CHOT luc publish (VD-M-15: phi CONG THEM vao ky
        /// quy, khong tru vao thu lao). Chot lai de admin doi phi ve sau khong lam
        /// thay doi hop dong da ky voi du an dang chay. 0 = chua publish.
        /// </summary>
        public int PlatformFeePercent { get; private set; }

        /// <summary>true tu luc ledger xac nhan giu tien. Huy luc do thi phai hoan tien.</summary>
        public bool WasEscrowed { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public DateTimeOffset UpdatedAt { get; private set; }

        /// <summary>Luc vao hang cho duyet — moc tinh 72 gio tu huy.</summary>
        public DateTimeOffset? SubmittedForApprovalAt { get; private set; }

        public DateTimeOffset? PublishedAt { get; private set; }

        public DateTimeOffset? ClosedAt { get; private set; }

        // =====================================================================
        // TAO VA CAU HINH — chi khi con Nhap
        // =====================================================================

        public static LabelingProject Tao(
            Guid ownerId,
            string name,
            string? description,
            TaskType taskType,
            ProjectVisibility visibility,
            DateTimeOffset luc)
        {
            if (ownerId == Guid.Empty)
            {
                throw new InvalidValueException("owner_rong", "Thieu chu so huu.");
            }

            // Hien chi ho tro phan loai anh: dataset nhan ZIP anh, schema la danh
            // sach lop. Loai khac can workspace va dinh dang nhan rieng (P6).
            if (taskType != TaskType.ImageClassification)
            {
                throw new InvalidValueException(
                    "loai_bai_toan_chua_ho_tro",
                    "Hien chi ho tro bai toan phan loai anh.");
            }

            LabelingProject duAn = new LabelingProject();

            duAn.Id = Guid.CreateVersion7();
            duAn.OwnerId = ownerId;
            duAn.TaskType = taskType;
            duAn.Status = ProjectStatus.Draft;
            duAn.CreatedAt = luc;
            duAn.GanThongTin(name, description, visibility, luc);

            // Mac dinh hop ly de doanh nghiep khong phai dien het moi thu.
            duAn.AllowProfessional = true;
            duAn.EntranceQuestionCount = 10;
            duAn.EntrancePassPercent = 80;

            return duAn;
        }

        public void CapNhatThongTin(string name, string? description, ProjectVisibility visibility, DateTimeOffset luc)
        {
            ChiKhiNhap("sua thong tin");
            GanThongTin(name, description, visibility, luc);
        }

        public void DatLabelSchema(LabelSchema schema, DateTimeOffset luc)
        {
            if (schema == null)
            {
                throw new ArgumentNullException(nameof(schema));
            }

            ChiKhiNhap("doi tap nhan");

            LabelSchema = schema;
            UpdatedAt = luc;
        }

        /// <summary>
        /// Duoc sua ca khi Tam dung: phat hien huong dan mo ho giua chung thi tam
        /// dung, lam ro, roi chay tiep. Khong cho sua khi dang chay — labeler dang
        /// lam bai se bi doi luat giua chung.
        /// </summary>
        public void DatHuongDan(Guideline guideline, DateTimeOffset luc)
        {
            if (guideline == null)
            {
                throw new ArgumentNullException(nameof(guideline));
            }

            if (Status != ProjectStatus.Draft && Status != ProjectStatus.Paused)
            {
                throw KhongTheKhi("sua huong dan");
            }

            foreach (GuidelineExample viDu in guideline.Examples)
            {
                if (viDu.Label != null && (LabelSchema == null || !LabelSchema.CoLop(viDu.Label)))
                {
                    throw new InvalidValueException(
                        "nhan_vi_du_khong_co",
                        "Vi du dung nhan '" + viDu.Label + "' khong co trong tap nhan.");
                }
            }

            Guideline = guideline;
            UpdatedAt = luc;
        }

        public void DatCauHinhGia(
            long unitPriceVnd,
            int redundancy,
            long budgetVnd,
            DateTimeOffset deadline,
            DateTimeOffset luc)
        {
            ChiKhiNhap("doi cau hinh gia");

            if (unitPriceVnd <= 0)
            {
                throw new InvalidValueException("don_gia_khong_hop_le", "Don gia phai lon hon 0.");
            }

            if (redundancy < 1 || redundancy > RedundancyToiDa)
            {
                throw new InvalidValueException(
                    "redundancy_khong_hop_le",
                    "So nguoi gan trung phai tu 1 den " + RedundancyToiDa + ".");
            }

            if (budgetVnd <= 0)
            {
                throw new InvalidValueException("ngan_sach_khong_hop_le", "Ngan sach phai lon hon 0.");
            }

            if (deadline <= luc)
            {
                throw new InvalidValueException("deadline_da_qua", "Deadline phai o tuong lai.");
            }

            UnitPriceVnd = unitPriceVnd;
            Redundancy = redundancy;
            BudgetVnd = budgetVnd;
            Deadline = deadline;
            UpdatedAt = luc;
        }

        public void DatKenhPhanPhoi(bool professional, bool linkGateway, bool collaborative, DateTimeOffset luc)
        {
            ChiKhiNhap("doi kenh phan phoi");

            if (!professional && !linkGateway && !collaborative)
            {
                throw new InvalidValueException("khong_co_kenh", "Phai chon it nhat mot kenh phan phoi.");
            }

            AllowProfessional = professional;
            AllowLinkGateway = linkGateway;
            AllowCollaborative = collaborative;
            UpdatedAt = luc;
        }

        public void DatDieuKienThamGia(
            int? minLevel,
            int? minReputation,
            bool requireEntranceTest,
            int entranceQuestionCount,
            int entrancePassPercent,
            DateTimeOffset luc)
        {
            ChiKhiNhap("doi dieu kien tham gia");

            if (minLevel.HasValue && minLevel.Value < 1)
            {
                throw new InvalidValueException("cap_do_khong_hop_le", "Cap do toi thieu phai tu 1.");
            }

            if (minReputation.HasValue && (minReputation.Value < 0 || minReputation.Value > 100))
            {
                throw new InvalidValueException("uy_tin_khong_hop_le", "Diem uy tin toi thieu phai tu 0 den 100.");
            }

            if (entranceQuestionCount < 1 || entranceQuestionCount > SoCauTestToiDa)
            {
                throw new InvalidValueException(
                    "so_cau_test_khong_hop_le",
                    "So cau test phai tu 1 den " + SoCauTestToiDa + ".");
            }

            if (entrancePassPercent < 1 || entrancePassPercent > 100)
            {
                throw new InvalidValueException("nguong_dau_khong_hop_le", "Nguong dau phai tu 1 den 100%.");
            }

            MinLevel = minLevel;
            MinReputation = minReputation;
            RequireEntranceTest = requireEntranceTest;
            EntranceQuestionCount = entranceQuestionCount;
            EntrancePassPercent = entrancePassPercent;
            UpdatedAt = luc;
        }

        /// <summary>Nap dataset chi khi Nhap: ky quy tinh theo so mau luc publish.</summary>
        public void KiemTraCoTheNapDuLieu()
        {
            ChiKhiNhap("nap them du lieu");
        }

        /// <summary>
        /// Cau hoi vang sua duoc khi Nhap hoac Tam dung — giong huong dan.
        /// </summary>
        public void KiemTraCoTheSuaCauHoiVang()
        {
            if (Status != ProjectStatus.Draft && Status != ProjectStatus.Paused)
            {
                throw KhongTheKhi("sua cau hoi vang");
            }

            if (LabelSchema == null)
            {
                throw new RuleViolationException(
                    "chua_co_tap_nhan",
                    "Phai dinh nghia tap nhan truoc khi tao cau hoi vang.");
            }
        }

        // =====================================================================
        // VONG DOI
        // =====================================================================

        /// <summary>
        /// Phi nen tang cho MOT nhan, so nguyen dong, lam tron XUONG (VD-M-07).
        /// Vd don gia 200.000d, phi 30% → 60.000d.
        /// </summary>
        public static long PhiMoiNhanVnd(long donGiaVnd, int phanTramPhi)
        {
            return checked(donGiaVnd * phanTramPhi) / 100;
        }

        /// <summary>
        /// Tien ky quy toi thieu (dac ta 2.11):
        ///     so mau x redundancy x (don gia + phi moi nhan)
        /// Vd 100 mau x 3 nguoi x (1.000 + 300) = 390.000d.
        /// Dung checked: tran so la NEM LOI, khong am tham quay vong thanh so am.
        /// </summary>
        public long ChiPhiUocTinhVnd(int soMau, int phanTramPhi)
        {
            long moiNhan = checked(UnitPriceVnd + PhiMoiNhanVnd(UnitPriceVnd, phanTramPhi));
            return checked(soMau * (long)Redundancy * moiNhan);
        }

        /// <summary>
        /// Danh sach nhung gi con thieu de publish. Rong = san sang.
        /// Frontend hien danh sach nay thanh checklist cho doanh nghiep.
        /// </summary>
        public IReadOnlyList<string> NhungGiConThieu(int soMau, int soCauVangChoTest, int phanTramPhi, DateTimeOffset luc)
        {
            List<string> thieu = new List<string>();

            if (LabelSchema == null)
            {
                thieu.Add("chua_co_tap_nhan");
            }

            if (Guideline == null)
            {
                thieu.Add("chua_co_huong_dan");
            }

            if (UnitPriceVnd <= 0 || Redundancy < 1 || BudgetVnd <= 0 || Deadline == null)
            {
                thieu.Add("chua_cau_hinh_gia");
            }
            else
            {
                if (Deadline.Value <= luc)
                {
                    thieu.Add("deadline_da_qua");
                }

                if (soMau > 0 && BudgetVnd < ChiPhiUocTinhVnd(soMau, phanTramPhi))
                {
                    thieu.Add("ngan_sach_khong_du");
                }
            }

            if (soMau <= 0)
            {
                thieu.Add("chua_co_du_lieu");
            }

            if (RequireEntranceTest && soCauVangChoTest < EntranceQuestionCount)
            {
                thieu.Add("thieu_cau_hoi_vang_cho_test");
            }

            return thieu;
        }

        /// <summary>
        /// Doanh nghiep bam publish: Nhap → Cho ky quy. Service phat
        /// project.publish_requested cho ledger (saga buoc 1).
        /// </summary>
        public void YeuCauPublish(int soMau, int soCauVangChoTest, int phanTramPhi, DateTimeOffset luc)
        {
            ChiKhiNhap("publish");

            if (phanTramPhi < 0 || phanTramPhi > 100)
            {
                throw new InvalidValueException("phi_khong_hop_le", "Phi nen tang phai tu 0 den 100%.");
            }

            IReadOnlyList<string> thieu = NhungGiConThieu(soMau, soCauVangChoTest, phanTramPhi, luc);
            if (thieu.Count > 0)
            {
                throw new RuleViolationException(
                    "chua_san_sang_publish",
                    "Chua the publish, con thieu: " + string.Join(", ", thieu));
            }

            PlatformFeePercent = phanTramPhi;
            DoiTrangThai(ProjectStatus.PendingEscrow, null, luc);
        }

        /// <summary>Ledger da giu tien (escrow.reserved): Cho ky quy → Cho duyet.</summary>
        public void XacNhanDaKyQuy(DateTimeOffset luc)
        {
            ChiKhi(ProjectStatus.PendingEscrow, "xac nhan ky quy");

            WasEscrowed = true;
            SubmittedForApprovalAt = luc;
            DoiTrangThai(ProjectStatus.PendingApproval, null, luc);
        }

        /// <summary>Ledger tu choi (escrow.rejected): ve Nhap kem ly do de doanh nghiep sua.</summary>
        public void KyQuyBiTuChoi(string lyDo, DateTimeOffset luc)
        {
            ChiKhi(ProjectStatus.PendingEscrow, "tu choi ky quy");
            DoiTrangThai(ProjectStatus.Draft, lyDo, luc);
        }

        /// <summary>Admin duyet (FM-02): Cho duyet → Dang chay.</summary>
        public void Duyet(DateTimeOffset luc)
        {
            ChiKhi(ProjectStatus.PendingApproval, "duyet");

            if (Deadline.HasValue && Deadline.Value <= luc)
            {
                throw new RuleViolationException(
                    "deadline_da_qua",
                    "Deadline da qua trong luc cho duyet — nen tu choi de hoan ky quy.");
            }

            PublishedAt = luc;
            DoiTrangThai(ProjectStatus.Running, null, luc);
        }

        /// <summary>
        /// Admin tu choi: Cho duyet → Huy. Da ky quy nen service phat
        /// project.cancelled de ledger hoan tien (compensation cua saga).
        /// </summary>
        public void TuChoiDuyet(string lyDo, DateTimeOffset luc)
        {
            ChiKhi(ProjectStatus.PendingApproval, "tu choi duyet");
            BatBuocCoLyDo(lyDo);

            ClosedAt = luc;
            DoiTrangThai(ProjectStatus.Cancelled, lyDo, luc);
        }

        /// <summary>Qua han cho duyet chua? Worker nen dung de tim du an can tu huy.</summary>
        public bool DaQuaHanChoDuyet(TimeSpan hanChoDuyet, DateTimeOffset luc)
        {
            return Status == ProjectStatus.PendingApproval
                   && SubmittedForApprovalAt.HasValue
                   && luc - SubmittedForApprovalAt.Value >= hanChoDuyet;
        }

        /// <summary>Compensation cua saga: khong ai duyet trong han → huy + hoan ky quy.</summary>
        public void HuyDoQuaHanChoDuyet(TimeSpan hanChoDuyet, DateTimeOffset luc)
        {
            if (!DaQuaHanChoDuyet(hanChoDuyet, luc))
            {
                throw new RuleViolationException(
                    "chua_qua_han",
                    "Du an chua qua han cho duyet.");
            }

            ClosedAt = luc;
            DoiTrangThai(ProjectStatus.Cancelled, "Qua han cho duyet, tu dong huy va hoan ky quy.", luc);
        }

        public void TamDung(DateTimeOffset luc)
        {
            ChiKhi(ProjectStatus.Running, "tam dung");
            DoiTrangThai(ProjectStatus.Paused, null, luc);
        }

        public void TiepTuc(DateTimeOffset luc)
        {
            ChiKhi(ProjectStatus.Paused, "tiep tuc");

            if (Deadline.HasValue && Deadline.Value <= luc)
            {
                throw new RuleViolationException(
                    "deadline_da_qua",
                    "Deadline da qua — hay hoan thanh du an thay vi chay tiep.");
            }

            DoiTrangThai(ProjectStatus.Running, null, luc);
        }

        public void HoanThanh(DateTimeOffset luc)
        {
            if (Status != ProjectStatus.Running && Status != ProjectStatus.Paused)
            {
                throw KhongTheKhi("hoan thanh");
            }

            ClosedAt = luc;
            DoiTrangThai(ProjectStatus.Completed, null, luc);
        }

        /// <summary>
        /// Doanh nghiep huy du an.
        ///
        /// KHONG cho huy luc Cho ky quy: yeu cau giu tien dang tren duong toi
        /// ledger. Huy luc nay thi escrow.reserved toi SAU se giu tien cho mot du
        /// an da chet. Doi vai giay cho ledger tra loi roi hay huy.
        /// </summary>
        public void Huy(string? lyDo, DateTimeOffset luc)
        {
            bool duocHuy = Status == ProjectStatus.Draft
                           || Status == ProjectStatus.PendingApproval
                           || Status == ProjectStatus.Running
                           || Status == ProjectStatus.Paused;

            if (!duocHuy)
            {
                throw KhongTheKhi("huy");
            }

            ClosedAt = luc;
            DoiTrangThai(ProjectStatus.Cancelled, lyDo, luc);
        }

        // =====================================================================
        // THAM GIA
        // =====================================================================

        /// <summary>
        /// Labeler tu tham gia (khong qua loi moi). Chi du an cong khai, dang
        /// chay, co kenh chuyen nghiep.
        /// </summary>
        public void KiemTraChoTuThamGia()
        {
            if (Status != ProjectStatus.Running)
            {
                throw new RuleViolationException("du_an_khong_chay", "Du an khong o trang thai nhan nguoi.");
            }

            if (Visibility == ProjectVisibility.Private)
            {
                throw new RuleViolationException("du_an_rieng_tu", "Du an rieng tu chi tham gia qua loi moi.");
            }

            if (!AllowProfessional)
            {
                throw new RuleViolationException(
                    "khong_mo_kenh_chuyen_nghiep",
                    "Du an khong nhan labeler chuyen nghiep.");
            }
        }

        public bool DaKetThuc()
        {
            return Status == ProjectStatus.Completed || Status == ProjectStatus.Cancelled;
        }

        // =====================================================================
        // Ham phu tro
        // =====================================================================

        private void GanThongTin(string name, string? description, ProjectVisibility visibility, DateTimeOffset luc)
        {
            string ten = name == null ? string.Empty : name.Trim();
            if (ten.Length == 0 || ten.Length > DoDaiTenToiDa)
            {
                throw new InvalidValueException(
                    "ten_khong_hop_le",
                    "Ten du an phai tu 1 den " + DoDaiTenToiDa + " ky tu.");
            }

            string moTa = description == null ? string.Empty : description.Trim();
            if (moTa.Length > DoDaiMoTaToiDa)
            {
                throw new InvalidValueException(
                    "mo_ta_qua_dai",
                    "Mo ta toi da " + DoDaiMoTaToiDa + " ky tu.");
            }

            Name = ten;
            Description = moTa;
            Visibility = visibility;
            UpdatedAt = luc;
        }

        private void ChiKhiNhap(string hanhDong)
        {
            ChiKhi(ProjectStatus.Draft, hanhDong);
        }

        private void ChiKhi(ProjectStatus canCo, string hanhDong)
        {
            if (Status != canCo)
            {
                throw KhongTheKhi(hanhDong);
            }
        }

        private RuleViolationException KhongTheKhi(string hanhDong)
        {
            return new RuleViolationException(
                "chuyen_trang_thai_khong_hop_le",
                "Khong the " + hanhDong + " khi du an dang o trang thai " + Status + ".");
        }

        private static void BatBuocCoLyDo(string lyDo)
        {
            if (string.IsNullOrWhiteSpace(lyDo))
            {
                throw new InvalidValueException("thieu_ly_do", "Phai ghi ly do.");
            }
        }

        private void DoiTrangThai(ProjectStatus moi, string? lyDo, DateTimeOffset luc)
        {
            Status = moi;
            StatusReason = lyDo;
            UpdatedAt = luc;
        }
    }
}
