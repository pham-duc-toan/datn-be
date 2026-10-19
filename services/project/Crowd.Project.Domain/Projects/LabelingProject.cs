using System;
using System.Collections.Generic;
using System.Linq;
using Crowd.Labeling;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.Members;

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



        /// <summary>EF Core dung constructor nay de dung lai doi tuong tu database.</summary>
        private List<string> _domains;
        private List<string> _languages;

        private LabelingProject()
        {
            Modality = string.Empty;
            Name = string.Empty;
            Description = string.Empty;
            _domains = new List<string>();
            _languages = new List<string>();
        }

        public Guid Id { get; private set; }

        /// <summary>Doanh nghiep tao du an. Cung duoc ghi thanh dong 'owner' trong project_members.</summary>
        public Guid OwnerId { get; private set; }

        public string Name { get; private set; }

        public string Description { get; private set; }

        /// <summary>
        /// LOAI DU LIEU cua du an: image | text | audio | video | pair
        /// (Crowd.Labeling.Modalities). Moi mau cua du an deu thuoc loai nay; nhan
        /// va dap an mang loai nay. Chon luc tao, khong doi duoc.
        /// </summary>
        public string Modality { get; private set; }

        public ProjectStatus Status { get; private set; }

        public ProjectVisibility Visibility { get; private set; }

        /// <summary>
        /// Tap nhan: danh sach cong cu (classification, bbox, span, transcription...)
        /// cho loai du lieu cua du an — xem Crowd.Labeling.LabelSchema. null = chua dinh nghia.
        /// </summary>
        public LabelSchema? LabelSchema { get; private set; }

        /// <summary>null = chua viet huong dan.</summary>
        public Guideline? Guideline { get; private set; }

        // ---- Cau hinh tien (FB-14) — theo CA DU AN (docs 3.18). Tien la long, so nguyen dong (VD-M-07). ----

        /// <summary>
        /// Tien thuong cho MOT labeler lam DAT ca du an (moi labeler lam het cac cau trong mot bai).
        /// 0 = chua cau hinh. Khong con don gia moi nhan.
        /// </summary>
        public long RewardVnd { get; private set; }

        /// <summary>So suat: toi da bay nhieu labeler lam bai. 0 = chua cau hinh.</summary>
        public int Slots { get; private set; }

        /// <summary>
        /// t — ti le % cau tra loi giong da so toi thieu de DAT (cung voi: khong sai cau vang nao).
        /// Doanh nghiep chon trong khoang [x, y] admin dat (project.pass_threshold_min / _max).
        /// </summary>
        public int PassThresholdPercent { get; private set; }

        public DateTimeOffset? Deadline { get; private set; }

        /// <summary>So cau that (mau khong phai cau vang) CHOT luc bam publish — hien o cho du an.</summary>
        public int QuestionCount { get; private set; }

        // ---- Kenh phan phoi (FB-17) ----

        public bool AllowProfessional { get; private set; }

        public bool AllowLinkGateway { get; private set; }

        public bool AllowCollaborative { get; private set; }

        /// <summary>
        /// Cong vuot link (kenh phu): tinh tien theo LUOT khach gan (khach chi lam 1-3 cau), khong theo
        /// ca bai. Gia moi nhan khach gan va ngan sach rieng cho cong link (nam trong ky quy).
        /// </summary>
        public long GateUnitPriceVnd { get; private set; }

        public long GateBudgetVnd { get; private set; }

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

        // ---- Bao ve nguoi gan nhan va du lieu (muc 2.9, FP-01, FP-05) ----

        /// <summary>
        /// Noi dung nhay cam (kiem duyet noi dung bao luc, kinh di...): chi labeler DA XAC MINH DU TUOI
        /// vao duoc, phai dong y canh bao truoc khi vao, frontend lam mo mac dinh, task-svc ep nghi giua phien.
        /// </summary>
        public bool SensitiveContent { get; private set; }

        /// <summary>Loi canh bao hien cho labeler truoc khi dong y (bat buoc khi SensitiveContent).</summary>
        public string? ContentWarning { get; private set; }

        /// <summary>Phai ky thoa thuan bao mat (NDA dien tu) truoc khi vao du an (FP-01).</summary>
        public bool NdaRequired { get; private set; }

        public string? NdaText { get; private set; }

        /// <summary>Tang moi lan sua van ban NDA — chu ky gan voi phien ban da ky.</summary>
        public int NdaVersion { get; private set; }

        /// <summary>Chi labeler da xac minh danh tinh (claim identity_verified) moi vao duoc (FP-05).</summary>
        public bool RequireVerifiedIdentity { get; private set; }

        // ---- Luu tru sau khi dong (muc 3.14) ----

        /// <summary>
        /// Doanh nghiep DONG Y xoa file du lieu goc (anh / am thanh / video trong kho) khi du an da dong qua
        /// project.closed_data_retention. Mac dinh KHONG xoa: du lieu la tai san cua doanh nghiep.
        /// </summary>
        public bool DeleteFilesAfterClose { get; private set; }

        /// <summary>Luc da xoa file trong kho; null = chua.</summary>
        public DateTimeOffset? FilesPurgedAt { get; private set; }

        // ---- Nhan de goi y du an (FL-02): so khop voi ho so ky nang cua labeler ----

        public IReadOnlyList<string> Domains
        {
            get { return _domains; }
        }

        public IReadOnlyList<string> Languages
        {
            get { return _languages; }
        }

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

        /// <summary>Doanh nghiep da bam "Ket thuc som" (cho task-svc dong thu bai). null = chua.</summary>
        public DateTimeOffset? FinishRequestedAt { get; private set; }

        /// <summary>Luc dong thu bai, bat dau cham (project.collection_finished).</summary>
        public DateTimeOffset? GradingStartedAt { get; private set; }

        // =====================================================================
        // TAO VA CAU HINH — chi khi con Nhap
        // =====================================================================

        public static LabelingProject Tao(
            Guid ownerId,
            string name,
            string? description,
            string modality,
            ProjectVisibility visibility,
            DateTimeOffset luc,
            QuyDinhDuAn quyDinh)
        {
            if (ownerId == Guid.Empty)
            {
                throw new InvalidValueException("owner_rong", "Thieu chu so huu.");
            }

            if (!Modalities.HopLe(modality))
            {
                throw new InvalidValueException(
                    "loai_du_lieu_khong_hop_le",
                    "Loai du lieu phai la mot trong: " + string.Join(", ", Modalities.TatCa) + ".");
            }

            LabelingProject duAn = new LabelingProject();

            duAn.Id = Guid.CreateVersion7();
            duAn.OwnerId = ownerId;
            duAn.Modality = modality;
            duAn.Status = ProjectStatus.Draft;
            duAn.CreatedAt = luc;
            duAn.GanThongTin(name, description, visibility, luc, quyDinh);

            // Mac dinh hop ly de doanh nghiep khong phai dien het moi thu.
            duAn.AllowProfessional = true;
            duAn.EntranceQuestionCount = quyDinh.SoCauTestMacDinh;
            duAn.EntrancePassPercent = quyDinh.NguongDauMacDinh;
            duAn.PassThresholdPercent = Math.Clamp(80, quyDinh.NguongDatToiThieu, quyDinh.NguongDatToiDa);

            return duAn;
        }

        public void CapNhatThongTin(string name, string? description, ProjectVisibility visibility, DateTimeOffset luc, QuyDinhDuAn quyDinh)
        {
            ChiKhiNhap("sua thong tin");
            GanThongTin(name, description, visibility, luc, quyDinh);
        }

        public void DatLabelSchema(LabelSchema schema, DateTimeOffset luc)
        {
            if (schema == null)
            {
                throw new ArgumentNullException(nameof(schema));
            }

            ChiKhiNhap("doi tap nhan");

            // Tap nhan cho anh khong dung duoc cho du an am thanh.
            if (schema.Modality != Modality)
            {
                throw new InvalidValueException(
                    "loai_du_lieu_khong_khop",
                    "Tap nhan danh cho du lieu " + schema.Modality + ", du an la du lieu " + Modality + ".");
            }

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

        /// <summary>
        /// Tien theo CA DU AN (docs 3.18): thuong moi nguoi dat, so suat, nguong t, han chot.
        /// Ky quy = so suat x (thuong + phi) (+ ngan sach cong link) — tinh tu dong, khong nhap tay.
        /// </summary>
        public void DatCauHinhGia(
            long rewardVnd,
            int slots,
            int passThresholdPercent,
            DateTimeOffset deadline,
            DateTimeOffset luc,
            QuyDinhDuAn quyDinh)
        {
            ChiKhiNhap("doi cau hinh gia");

            if (rewardVnd <= 0)
            {
                throw new InvalidValueException("thuong_khong_hop_le", "Tien thuong phai lon hon 0.");
            }

            if (rewardVnd < quyDinh.ThuongToiThieu)
            {
                throw new InvalidValueException(
                    "thuong_duoi_toi_thieu",
                    "Tien thuong toi thieu cua nen tang la " + quyDinh.ThuongToiThieu + "d cho moi nguoi lam dat.");
            }

            if (slots < quyDinh.SoSuatToiThieu || slots > quyDinh.SoSuatToiDa)
            {
                throw new InvalidValueException(
                    "so_suat_khong_hop_le",
                    "So suat phai tu " + quyDinh.SoSuatToiThieu + " den " + quyDinh.SoSuatToiDa + " nguoi.");
            }

            if (passThresholdPercent < quyDinh.NguongDatToiThieu || passThresholdPercent > quyDinh.NguongDatToiDa)
            {
                throw new InvalidValueException(
                    "nguong_dat_ngoai_khoang",
                    "Nguong dat phai tu " + quyDinh.NguongDatToiThieu + "% den " + quyDinh.NguongDatToiDa + "%.");
            }

            if (deadline <= luc)
            {
                throw new InvalidValueException("deadline_da_qua", "Deadline phai o tuong lai.");
            }

            RewardVnd = rewardVnd;
            Slots = slots;
            PassThresholdPercent = passThresholdPercent;
            Deadline = deadline;
            UpdatedAt = luc;
        }

        /// <param name="gateUnitPriceVnd">Gia moi nhan khach vuot link gan (chi khi bat cong link).</param>
        /// <param name="gateBudgetVnd">Ngan sach rieng cua cong link (chi khi bat cong link).</param>
        public void DatKenhPhanPhoi(
            bool professional, bool linkGateway, bool collaborative, long? gateUnitPriceVnd, long? gateBudgetVnd, DateTimeOffset luc)
        {
            ChiKhiNhap("doi kenh phan phoi");

            if (!professional && !linkGateway && !collaborative)
            {
                throw new InvalidValueException("khong_co_kenh", "Phai chon it nhat mot kenh phan phoi.");
            }

            long gia = gateUnitPriceVnd ?? 0;
            long nganSach = gateBudgetVnd ?? 0;
            if (gia < 0 || nganSach < 0)
            {
                throw new InvalidValueException("gia_cong_link_khong_hop_le", "Gia va ngan sach cong link khong duoc am.");
            }

            if (linkGateway && nganSach > 0 && gia > 0 && nganSach < gia)
            {
                throw new InvalidValueException("ngan_sach_cong_link_qua_nho", "Ngan sach cong link phai du it nhat mot nhan.");
            }

            AllowProfessional = professional;
            AllowLinkGateway = linkGateway;
            AllowCollaborative = collaborative;
            GateUnitPriceVnd = linkGateway ? gia : 0;
            GateBudgetVnd = linkGateway ? nganSach : 0;
            UpdatedAt = luc;
        }

        public void DatDieuKienThamGia(
            int? minLevel,
            int? minReputation,
            bool requireEntranceTest,
            int entranceQuestionCount,
            int entrancePassPercent,
            DateTimeOffset luc,
            QuyDinhDuAn quyDinh)
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

            if (entranceQuestionCount < 1 || entranceQuestionCount > quyDinh.SoCauTestToiDa)
            {
                throw new InvalidValueException(
                    "so_cau_test_khong_hop_le",
                    "So cau test phai tu 1 den " + quyDinh.SoCauTestToiDa + ".");
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

        /// <summary>
        /// Cau hinh bao ve (muc 2.9, FP-01, FP-05) — chi khi Nhap: labeler da dong y / da ky theo dieu
        /// khoan cu khong duoc doi luat giua chung.
        /// </summary>
        public void DatBaoVe(
            bool sensitiveContent,
            string? contentWarning,
            bool ndaRequired,
            string? ndaText,
            bool requireVerifiedIdentity,
            DateTimeOffset luc)
        {
            ChiKhiNhap("doi cau hinh bao ve");

            string? canhBao = string.IsNullOrWhiteSpace(contentWarning) ? null : contentWarning.Trim();
            if (sensitiveContent && canhBao == null)
            {
                throw new InvalidValueException("thieu_canh_bao", "Du an noi dung nhay cam phai co loi canh bao cho labeler.");
            }

            if (canhBao != null && canhBao.Length > CotDb.CanhBaoNoiDung)
            {
                throw new InvalidValueException("canh_bao_qua_dai", "Loi canh bao toi da " + CotDb.CanhBaoNoiDung + " ky tu.");
            }

            string? nda = string.IsNullOrWhiteSpace(ndaText) ? null : ndaText.Trim();
            if (ndaRequired && nda == null)
            {
                throw new InvalidValueException("thieu_van_ban_nda", "Bat NDA thi phai co van ban thoa thuan.");
            }

            if (nda != null && nda.Length > CotDb.VanBanNda)
            {
                throw new InvalidValueException("nda_qua_dai", "Van ban NDA toi da " + CotDb.VanBanNda + " ky tu.");
            }

            if (!string.Equals(nda, NdaText, StringComparison.Ordinal))
            {
                NdaVersion++;
            }

            SensitiveContent = sensitiveContent;
            ContentWarning = sensitiveContent ? canhBao : null;
            NdaRequired = ndaRequired;
            NdaText = nda;
            RequireVerifiedIdentity = requireVerifiedIdentity;
            UpdatedAt = luc;
        }

        /// <summary>Can dong y CA NHAN truoc khi vao (canh bao noi dung / NDA) — chu du an khong dong y thay duoc.</summary>
        public bool CanDongYCaNhan()
        {
            return SensitiveContent || NdaRequired;
        }

        /// <summary>
        /// Dieu kien bao ve cho MOT labeler: null = du. Thu tu: xac minh danh tinh → du tuoi → dong y.
        /// </summary>
        public string? LyDoKhongDuDieuKienBaoVe(bool daXacMinhDanhTinh, bool duTuoi, bool daDongYDieuKhoan)
        {
            if ((RequireVerifiedIdentity || SensitiveContent) && !daXacMinhDanhTinh)
            {
                return "can_xac_minh_danh_tinh";
            }

            if (SensitiveContent && !duTuoi)
            {
                return "chua_du_tuoi";
            }

            if (CanDongYCaNhan() && !daDongYDieuKhoan)
            {
                return "can_dong_y_dieu_khoan";
            }

            return null;
        }

        public static string GiaiThichBaoVe(string lyDo)
        {
            switch (lyDo)
            {
                case "can_xac_minh_danh_tinh":
                    return "Du an chi nhan labeler da xac minh danh tinh (gui anh giay to o /me/identity-verification).";
                case "chua_du_tuoi":
                    return "Du an co noi dung nhay cam, chi nhan nguoi da xac minh du tuoi.";
                case "can_dong_y_dieu_khoan":
                    return "Doc va dong y canh bao noi dung / ky NDA truoc (POST /projects/{id}/consent).";
                default:
                    return "Khong du dieu kien bao ve cua du an.";
            }
        }

        /// <summary>Linh vuc / ngon ngu cua du an (FL-02). Doi duoc moi luc chua ket thuc — chi anh huong goi y.</summary>
        public void DatNhan(IReadOnlyList<string>? linhVuc, IReadOnlyList<string>? ngonNgu, DateTimeOffset luc)
        {
            if (DaKetThuc())
            {
                throw KhongTheKhi("doi nhan");
            }

            _domains = ChuanHoaNhan(linhVuc, "Linh vuc");
            _languages = ChuanHoaNhan(ngonNgu, "Ngon ngu");
            UpdatedAt = luc;
        }

        private static List<string> ChuanHoaNhan(IReadOnlyList<string>? ds, string ten)
        {
            List<string> kq = new List<string>();
            HashSet<string> daCo = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (ds == null)
            {
                return kq;
            }

            foreach (string? muc in ds)
            {
                string v = (muc ?? string.Empty).Trim();
                if (v.Length == 0 || v.Length > 50)
                {
                    throw new InvalidValueException("nhan_khong_hop_le", ten + ": moi muc tu 1 den 50 ky tu.");
                }

                if (daCo.Add(v))
                {
                    kq.Add(v);
                }
            }

            if (kq.Count > 10)
            {
                throw new InvalidValueException("nhan_khong_hop_le", ten + ": toi da 10 muc.");
            }

            return kq;
        }

        /// <summary>Bat / tat dong y xoa file sau khi dong. Da xoa roi thi khong doi duoc nua.</summary>
        public void DatXoaFileSauKhiDong(bool dongY, DateTimeOffset luc)
        {
            if (FilesPurgedAt.HasValue)
            {
                throw new RuleViolationException("file_da_xoa", "File cua du an da bi xoa.");
            }

            DeleteFilesAfterClose = dongY;
            UpdatedAt = luc;
        }

        /// <summary>Den han xoa file: da dong, doanh nghiep dong y, qua han giu, chua xoa.</summary>
        public bool DenHanXoaFile(TimeSpan hanGiu, DateTimeOffset luc)
        {
            return DaKetThuc() && DeleteFilesAfterClose && !FilesPurgedAt.HasValue
                && ClosedAt.HasValue && hanGiu > TimeSpan.Zero && luc - ClosedAt.Value >= hanGiu;
        }

        public void DanhDauDaXoaFile(DateTimeOffset luc)
        {
            FilesPurgedAt = luc;
        }

        /// <summary>Nap dataset chi khi Nhap: ky quy tinh theo so mau luc publish.</summary>
        public void KiemTraCoTheNapDuLieu()
        {
            ChiKhiNhap("nap them du lieu");
        }

        /// <summary>
        /// Cau hoi vang CHI sua duoc khi Nhap: moi bai lam deu cham theo cung bo cau vang — doi giua
        /// chung thi nguoi lam truoc va nguoi lam sau bi cham theo hai luat khac nhau.
        /// </summary>
        public void KiemTraCoTheSuaCauHoiVang()
        {
            if (Status != ProjectStatus.Draft)
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
        /// Phi nen tang tinh tren mot khoan tien (thuong moi nguoi dat, hoac gia moi nhan cong link), so
        /// nguyen dong, lam tron XUONG (VD-M-07). Vd thuong 200.000d, phi 30% → 60.000d.
        /// </summary>
        public static long TinhPhiVnd(long soTienVnd, int phanTramPhi)
        {
            return checked(soTienVnd * phanTramPhi) / 100;
        }

        /// <summary>
        /// Tien ky quy (dac ta 2.11, ban du an lam trung tam):
        ///     so suat x (thuong + phi moi nguoi) + ngan sach cong link (neu bat)
        /// Vd 5 suat x (200.000 + 60.000) = 1.300.000d. Suat khong ai dat → hoan khi du an ket thuc.
        /// Dung checked: tran so la NEM LOI, khong am tham quay vong thanh so am.
        /// </summary>
        public long ChiPhiUocTinhVnd(int phanTramPhi)
        {
            long moiNguoi = checked(RewardVnd + TinhPhiVnd(RewardVnd, phanTramPhi));
            long congLink = AllowLinkGateway ? GateBudgetVnd : 0;
            return checked(Slots * moiNguoi + congLink);
        }

        /// <summary>
        /// Danh sach nhung gi con thieu de publish. Rong = san sang. Frontend hien thanh checklist.
        /// </summary>
        /// <param name="soCauThat">So mau KHONG phai cau vang (moi labeler lam het).</param>
        /// <param name="coDuLieuDangXuLy">Con lo du lieu dang xu ly — chua biet het so cau.</param>
        /// <param name="congCuChoCongLink">Loai cong cu khach vang lai lam duoc (gate.allowed_tools); null = khong kiem.</param>
        public IReadOnlyList<string> NhungGiConThieu(
            int soCauThat, int soCauVangChoTest, bool coDuLieuDangXuLy, QuyDinhDuAn quyDinh,
            IReadOnlyCollection<string>? congCuChoCongLink, DateTimeOffset luc)
        {
            if (quyDinh == null)
            {
                throw new ArgumentNullException(nameof(quyDinh));
            }

            List<string> thieu = new List<string>();

            if (congCuChoCongLink != null && CongCuKhongHopCongLink(congCuChoCongLink).Count > 0)
            {
                thieu.Add("cong_cu_khong_hop_cong_link");
            }

            if (AllowLinkGateway && (GateUnitPriceVnd <= 0 || GateBudgetVnd <= 0))
            {
                thieu.Add("chua_cau_hinh_cong_link");
            }

            if (coDuLieuDangXuLy)
            {
                thieu.Add("du_lieu_dang_xu_ly");
            }

            if (LabelSchema == null)
            {
                thieu.Add("chua_co_tap_nhan");
            }

            if (Guideline == null)
            {
                thieu.Add("chua_co_huong_dan");
            }

            if (RewardVnd <= 0 || Slots < 1 || Deadline == null)
            {
                thieu.Add("chua_cau_hinh_gia");
            }
            else
            {
                if (Deadline.Value <= luc)
                {
                    thieu.Add("deadline_da_qua");
                }

                // Admin doi gioi han sau khi doanh nghiep cau hinh → chan publish, doanh nghiep sua lai.
                if (RewardVnd < quyDinh.ThuongToiThieu)
                {
                    thieu.Add("thuong_duoi_toi_thieu");
                }

                if (Slots < quyDinh.SoSuatToiThieu || Slots > quyDinh.SoSuatToiDa)
                {
                    thieu.Add("so_suat_khong_hop_le");
                }

                if (PassThresholdPercent < quyDinh.NguongDatToiThieu || PassThresholdPercent > quyDinh.NguongDatToiDa)
                {
                    thieu.Add("nguong_dat_ngoai_khoang");
                }
            }

            if (soCauThat <= 0)
            {
                thieu.Add("chua_co_du_lieu");
            }
            else if (soCauThat > quyDinh.SoCauToiDa)
            {
                thieu.Add("qua_nhieu_cau");
            }

            if (RequireEntranceTest && soCauVangChoTest < EntranceQuestionCount)
            {
                thieu.Add("thieu_cau_hoi_vang_cho_test");
            }

            return thieu;
        }

        /// <summary>Canh bao KHONG chan publish: khong co cau vang kiem tra thi luat "khong sai cau vang" vo nghia.</summary>
        public static IReadOnlyList<string> CanhBao(int soCauVangKiemTra)
        {
            return CanhBao(soCauVangKiemTra, null);
        }

        /// <summary>
        /// Nhu tren, them: tap nhan co cau tra loi mo (freeText) → "cham_cau_tra_loi_mo_lau" — khi du an ket thuc,
        /// quality-svc cho ml-svc phan tich nghia tung cau roi moi cham (kien truc 3.19), ket qua va tien cong
        /// cua labeler co muon hon cac dang khac.
        /// </summary>
        public static IReadOnlyList<string> CanhBao(int soCauVangKiemTra, LabelSchema? tapNhan)
        {
            List<string> ds = new List<string>();
            if (soCauVangKiemTra <= 0)
            {
                ds.Add("khong_co_cau_vang_kiem_tra");
            }

            if (tapNhan != null && tapNhan.Tools.Any(t => t.Kind == ToolKinds.FreeText))
            {
                ds.Add("cham_cau_tra_loi_mo_lau");
            }

            return ds;
        }

        /// <summary>
        /// Cong cu cua tap nhan ma khach vang lai KHONG lam duoc o trang vuot link (chi khi bat cong link). Khach
        /// chi co vai giay, khong co huong dan dai, khong co uy tin: ve da giac / mat na / chuoi khung, chep loi dai...
        /// thi gan nhu chac chan sai — bat cong link cho cac cong cu do chi dot ngan sach. Tra ten cong cu.
        /// </summary>
        public IReadOnlyList<string> CongCuKhongHopCongLink(IReadOnlyCollection<string> choPhep)
        {
            List<string> ra = new List<string>();
            if (!AllowLinkGateway || LabelSchema == null || choPhep == null)
            {
                return ra;
            }

            foreach (ToolDefinition t in LabelSchema.Tools)
            {
                if (!choPhep.Contains(t.Kind))
                {
                    ra.Add(t.Name);
                }
            }

            return ra;
        }

        /// <summary>
        /// Doanh nghiep bam publish: Nhap → Cho ky quy. Service phat project.publish_requested cho ledger
        /// (saga buoc 1). Chot phi nen tang va so cau that.
        /// </summary>
        public void YeuCauPublish(
            int soCauThat, int soCauVangChoTest, int phanTramPhi, bool coDuLieuDangXuLy, QuyDinhDuAn quyDinh,
            IReadOnlyCollection<string>? congCuChoCongLink, DateTimeOffset luc)
        {
            ChiKhiNhap("publish");

            if (phanTramPhi < 0 || phanTramPhi > 100)
            {
                throw new InvalidValueException("phi_khong_hop_le", "Phi nen tang phai tu 0 den 100%.");
            }

            IReadOnlyList<string> thieu = NhungGiConThieu(soCauThat, soCauVangChoTest, coDuLieuDangXuLy, quyDinh, congCuChoCongLink, luc);
            if (thieu.Count > 0)
            {
                throw new RuleViolationException(
                    "chua_san_sang_publish",
                    "Chua the publish, con thieu: " + string.Join(", ", thieu));
            }

            PlatformFeePercent = phanTramPhi;
            QuestionCount = soCauThat;
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

        /// <summary>
        /// Doanh nghiep bam "Ket thuc som" (Dang chay / Tam dung): task-svc dong thu bai NGAY, bai dang lam
        /// do bi huy, roi cham voi so bai da nop. Bam lai khi da yeu cau → 409.
        /// </summary>
        public void YeuCauKetThucSom(DateTimeOffset luc)
        {
            if (Status != ProjectStatus.Running && Status != ProjectStatus.Paused)
            {
                throw KhongTheKhi("ket thuc som");
            }

            if (FinishRequestedAt.HasValue)
            {
                throw new RuleViolationException("da_yeu_cau_ket_thuc", "Da yeu cau ket thuc, he thong dang dong thu bai.");
            }

            FinishRequestedAt = luc;
            UpdatedAt = luc;
        }

        /// <summary>
        /// task-svc dong thu bai (project.collection_finished: du suat / het han / ket thuc som):
        /// Dang chay / Tam dung → Dang cham. Event den lai (da cham / da xong) → false, khong lam gi.
        /// </summary>
        public bool BatDauCham(DateTimeOffset luc)
        {
            if (Status == ProjectStatus.Grading || DaKetThuc())
            {
                return false;
            }

            if (Status != ProjectStatus.Running && Status != ProjectStatus.Paused)
            {
                throw KhongTheKhi("bat dau cham");
            }

            GradingStartedAt = luc;
            DoiTrangThai(ProjectStatus.Grading, null, luc);
            return true;
        }

        /// <summary>
        /// Da cham, het han khieu nai, khong con khieu nai mo (project.settled): Dang cham → Hoan thanh.
        /// Event den lai → false.
        /// </summary>
        public bool HoanThanh(DateTimeOffset luc)
        {
            if (Status == ProjectStatus.Completed)
            {
                return false;
            }

            ChiKhi(ProjectStatus.Grading, "hoan thanh");
            ClosedAt = luc;
            DoiTrangThai(ProjectStatus.Completed, null, luc);
            return true;
        }

        /// <summary>
        /// Doanh nghiep huy du an. Du an da chay phai TAM DUNG truoc va CHUA CO BAI NAO NOP (service hoi
        /// task-svc) — co bai nop thi phai "ket thuc som" de cham, khong de ai lam khong cong.
        ///
        /// KHONG cho huy luc Cho ky quy: yeu cau giu tien dang tren duong toi
        /// ledger. Huy luc nay thi escrow.reserved toi SAU se giu tien cho mot du
        /// an da chet. Doi vai giay cho ledger tra loi roi hay huy.
        /// </summary>
        public void Huy(string? lyDo, DateTimeOffset luc)
        {
            if (Status == ProjectStatus.Running)
            {
                throw CanTamDungTruoc("huy");
            }

            bool duocHuy = Status == ProjectStatus.Draft
                           || Status == ProjectStatus.PendingApproval
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
        /// Labeler tu tham gia THANG (POST /join, khong qua loi moi). Chi du an cong khai
        /// khong can duyet, dang chay, co kenh chuyen nghiep.
        /// </summary>
        public void KiemTraChoTuThamGia()
        {
            KiemTraChoXinThamGia();

            if (Visibility == ProjectVisibility.Approval)
            {
                throw new RuleViolationException(
                    "can_xin_duyet",
                    "Du an can chu du an duyet — gui don xin tham gia (POST /projects/{id}/join-requests).");
            }
        }

        /// <summary>
        /// Labeler tu tim den du an: lam bai test dau vao, gui don xin tham gia, hoac join
        /// thang. Du an phai dang chay, khong rieng tu, co kenh chuyen nghiep.
        /// </summary>
        public void KiemTraChoXinThamGia()
        {
            if (Status != ProjectStatus.Running)
            {
                throw new RuleViolationException("du_an_khong_chay", "Du an khong o trang thai nhan nguoi.");
            }

            if (Visibility == ProjectVisibility.Private)
            {
                throw new RuleViolationException("du_an_rieng_tu", "Du an rieng tu chi tham gia qua loi moi.");
            }

            KiemTraKenhChuyenNghiep();
        }

        /// <summary>
        /// Vao bang MA MOI cua chu du an: duoc o moi trang thai chua ket thuc (moi truoc khi
        /// publish cung duoc, nhu moi truc tiep), bo qua che do hien thi va bai test — ma moi
        /// la loi moi cua chu du an.
        /// </summary>
        public void KiemTraChoVaoBangMaMoi()
        {
            if (DaKetThuc())
            {
                throw new RuleViolationException("du_an_da_ket_thuc", "Du an da ket thuc.");
            }

            KiemTraKenhChuyenNghiep();
        }

        /// <summary>
        /// Labeler nay du dieu kien cua du an khong — CUNG luat va CUNG ma loi voi task-svc khi
        /// cap task (EligibilityPolicy), de nguoi khong du dieu kien bi chan ngay luc vao chu
        /// khong phai vao roi moi bi tu choi o moi lan nhan task. null = du dieu kien.
        /// </summary>
        public string? LyDoLabelerKhongDuDieuKien(HoSoNguoiDung hoSo)
        {
            if (hoSo == null)
            {
                throw new ArgumentNullException(nameof(hoSo));
            }

            if (hoSo.BiKhoa)
            {
                return "tai_khoan_bi_khoa";
            }

            if (!hoSo.LaLabeler)
            {
                return "khong_phai_labeler";
            }

            if (MinLevel.HasValue && hoSo.CapDo < MinLevel.Value)
            {
                return "chua_du_cap_do";
            }

            if (MinReputation.HasValue && (!hoSo.UyTin.HasValue || hoSo.UyTin.Value < MinReputation.Value))
            {
                return "chua_du_uy_tin";
            }

            return null;
        }

        /// <summary>Nhu LyDoLabelerKhongDuDieuKien nhung nem 409 kem ma va loi giai thich.</summary>
        public void KiemTraLabelerDuDieuKien(HoSoNguoiDung hoSo)
        {
            string? lyDo = LyDoLabelerKhongDuDieuKien(hoSo);
            if (lyDo != null)
            {
                throw new RuleViolationException(lyDo, GiaiThichKhongDuDieuKien(lyDo, hoSo));
            }
        }

        public string GiaiThichKhongDuDieuKien(string lyDo, HoSoNguoiDung hoSo)
        {
            if (hoSo == null)
            {
                throw new ArgumentNullException(nameof(hoSo));
            }

            switch (lyDo)
            {
                case "tai_khoan_bi_khoa":
                    return "Tai khoan dang bi khoa.";
                case "khong_phai_labeler":
                    return "Tai khoan khong co vai tro labeler.";
                case "chua_du_cap_do":
                    return "Du an yeu cau cap do toi thieu " + MinLevel + ", hien tai la " + hoSo.CapDo + ".";
                case "chua_du_uy_tin":
                    return hoSo.UyTin.HasValue
                        ? "Du an yeu cau uy tin toi thieu " + MinReputation + ", hien tai la " + hoSo.UyTin.Value + "."
                        : "Du an yeu cau uy tin toi thieu " + MinReputation + ", tai khoan chua co diem uy tin.";
                default:
                    return "Khong du dieu kien tham gia du an.";
            }
        }

        public bool DaKetThuc()
        {
            return Status == ProjectStatus.Completed || Status == ProjectStatus.Cancelled;
        }

        // =====================================================================
        // Ham phu tro
        // =====================================================================

        private void GanThongTin(string name, string? description, ProjectVisibility visibility, DateTimeOffset luc, QuyDinhDuAn quyDinh)
        {
            string ten = name == null ? string.Empty : name.Trim();
            if (ten.Length == 0 || ten.Length > quyDinh.DoDaiTenToiDa)
            {
                throw new InvalidValueException(
                    "ten_khong_hop_le",
                    "Ten du an phai tu 1 den " + quyDinh.DoDaiTenToiDa + " ky tu.");
            }

            string moTa = description == null ? string.Empty : description.Trim();
            if (moTa.Length > quyDinh.DoDaiMoTaToiDa)
            {
                throw new InvalidValueException(
                    "mo_ta_qua_dai",
                    "Mo ta toi da " + quyDinh.DoDaiMoTaToiDa + " ky tu.");
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

        private void KiemTraKenhChuyenNghiep()
        {
            if (!AllowProfessional)
            {
                throw new RuleViolationException(
                    "khong_mo_kenh_chuyen_nghiep",
                    "Du an khong nhan labeler chuyen nghiep.");
            }
        }

        private static RuleViolationException CanTamDungTruoc(string hanhDong)
        {
            return new RuleViolationException(
                "can_tam_dung_truoc",
                "Hay tam dung du an truoc khi " + hanhDong + ": ngung nhan nguoi lam va nop bai moi.");
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
