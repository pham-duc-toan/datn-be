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

        /// <summary>
        /// Lap suat trong: cham xong ma so bai DAT it hon so suat thi cac suat con lai trao cho bai giong da so
        /// nhieu nhat (cung % thi nop som hon), bai phai dung het cau vang va giong da so tu muc nay tro len.
        /// null = khong lap (bai khong dat thi khong duoc thuong).
        /// </summary>
        public int? FillSlotsMinPercent { get; private set; }

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
                throw new InvalidValueException("owner_rong", "Thiếu chủ sở hữu.");
            }

            if (!Modalities.HopLe(modality))
            {
                throw new InvalidValueException(
                    "loai_du_lieu_khong_hop_le",
                    "Loại dữ liệu phải là một trong: " + string.Join(", ", Modalities.TatCa) + ".");
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
            ChiKhiNhap("sửa thông tin");
            GanThongTin(name, description, visibility, luc, quyDinh);
        }

        public void DatLabelSchema(LabelSchema schema, DateTimeOffset luc)
        {
            if (schema == null)
            {
                throw new ArgumentNullException(nameof(schema));
            }

            ChiKhiNhap("đổi tập nhãn");

            // Tap nhan cho anh khong dung duoc cho du an am thanh.
            if (schema.Modality != Modality)
            {
                throw new InvalidValueException(
                    "loai_du_lieu_khong_khop",
                    "Tập nhãn dành cho dữ liệu " + schema.Modality + ", dự án là dữ liệu " + Modality + ".");
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
                throw KhongTheKhi("sửa hướng dẫn");
            }

            foreach (GuidelineExample viDu in guideline.Examples)
            {
                if (viDu.Label != null && (LabelSchema == null || !LabelSchema.CoLop(viDu.Label)))
                {
                    throw new InvalidValueException(
                        "nhan_vi_du_khong_co",
                        "Ví dụ dùng nhãn '" + viDu.Label + "' không có trong tập nhãn.");
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
            DatCauHinhGia(rewardVnd, slots, passThresholdPercent, null, deadline, luc, quyDinh);
        }

        /// <param name="fillSlotsMinPercent">Lap suat trong (xem FillSlotsMinPercent): null = khong lap; 0..t.</param>
        public void DatCauHinhGia(
            long rewardVnd,
            int slots,
            int passThresholdPercent,
            int? fillSlotsMinPercent,
            DateTimeOffset deadline,
            DateTimeOffset luc,
            QuyDinhDuAn quyDinh)
        {
            ChiKhiNhap("đổi cấu hình giá");

            if (fillSlotsMinPercent.HasValue && (fillSlotsMinPercent.Value < 0 || fillSlotsMinPercent.Value > passThresholdPercent))
            {
                throw new InvalidValueException(
                    "san_lap_suat_khong_hop_le",
                    "Mức giống đa số tối thiểu để lấp suất trống phải từ 0% đến ngưỡng đạt (" + passThresholdPercent + "%).");
            }

            if (rewardVnd <= 0)
            {
                throw new InvalidValueException("thuong_khong_hop_le", "Tiền thưởng phải lớn hơn 0.");
            }

            if (rewardVnd < quyDinh.ThuongToiThieu)
            {
                throw new InvalidValueException(
                    "thuong_duoi_toi_thieu",
                    "Tiền thưởng tối thiểu của nền tảng là " + quyDinh.ThuongToiThieu + "đ cho mỗi người làm đạt.");
            }

            if (slots < quyDinh.SoSuatToiThieu || slots > quyDinh.SoSuatToiDa)
            {
                throw new InvalidValueException(
                    "so_suat_khong_hop_le",
                    "Số suất thưởng phải từ " + quyDinh.SoSuatToiThieu + " đến " + quyDinh.SoSuatToiDa + " người.");
            }

            if (passThresholdPercent < quyDinh.NguongDatToiThieu || passThresholdPercent > quyDinh.NguongDatToiDa)
            {
                throw new InvalidValueException(
                    "nguong_dat_ngoai_khoang",
                    "Ngưỡng đạt phải từ " + quyDinh.NguongDatToiThieu + "% đến " + quyDinh.NguongDatToiDa + "%.");
            }

            if (deadline <= luc)
            {
                throw new InvalidValueException("deadline_da_qua", "Hạn chót phải ở tương lai.");
            }

            RewardVnd = rewardVnd;
            Slots = slots;
            PassThresholdPercent = passThresholdPercent;
            FillSlotsMinPercent = fillSlotsMinPercent;
            Deadline = deadline;
            UpdatedAt = luc;
        }

        /// <param name="gateUnitPriceVnd">Gia moi nhan khach vuot link gan (chi khi bat cong link).</param>
        /// <param name="gateBudgetVnd">Ngan sach rieng cua cong link (chi khi bat cong link).</param>
        public void DatKenhPhanPhoi(
            bool professional, bool linkGateway, bool collaborative, long? gateUnitPriceVnd, long? gateBudgetVnd, DateTimeOffset luc)
        {
            ChiKhiNhap("đổi kênh phân phối");

            if (!professional && !linkGateway && !collaborative)
            {
                throw new InvalidValueException("khong_co_kenh", "Phải chọn ít nhất một kênh phân phối.");
            }

            long gia = gateUnitPriceVnd ?? 0;
            long nganSach = gateBudgetVnd ?? 0;
            if (gia < 0 || nganSach < 0)
            {
                throw new InvalidValueException("gia_cong_link_khong_hop_le", "Giá và ngân sách cổng link không được âm.");
            }

            if (linkGateway && nganSach > 0 && gia > 0 && nganSach < gia)
            {
                throw new InvalidValueException("ngan_sach_cong_link_qua_nho", "Ngân sách cổng link phải đủ ít nhất một nhãn.");
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
            ChiKhiNhap("đổi điều kiện tham gia");

            if (minLevel.HasValue && minLevel.Value < 1)
            {
                throw new InvalidValueException("cap_do_khong_hop_le", "Cấp độ tối thiểu phải từ 1.");
            }

            if (minReputation.HasValue && (minReputation.Value < 0 || minReputation.Value > 100))
            {
                throw new InvalidValueException("uy_tin_khong_hop_le", "Điểm uy tín tối thiểu phải từ 0 đến 100.");
            }

            if (entranceQuestionCount < 1 || entranceQuestionCount > quyDinh.SoCauTestToiDa)
            {
                throw new InvalidValueException(
                    "so_cau_test_khong_hop_le",
                    "Số câu test phải từ 1 đến " + quyDinh.SoCauTestToiDa + ".");
            }

            if (entrancePassPercent < 1 || entrancePassPercent > 100)
            {
                throw new InvalidValueException("nguong_dau_khong_hop_le", "Ngưỡng đậu phải từ 1 đến 100%.");
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
            ChiKhiNhap("đổi cấu hình bảo vệ");

            string? canhBao = string.IsNullOrWhiteSpace(contentWarning) ? null : contentWarning.Trim();
            if (sensitiveContent && canhBao == null)
            {
                throw new InvalidValueException("thieu_canh_bao", "Dự án nội dung nhạy cảm phải có lời cảnh báo cho labeler.");
            }

            if (canhBao != null && canhBao.Length > CotDb.CanhBaoNoiDung)
            {
                throw new InvalidValueException("canh_bao_qua_dai", "Lời cảnh báo tối đa " + CotDb.CanhBaoNoiDung + " ký tự.");
            }

            string? nda = string.IsNullOrWhiteSpace(ndaText) ? null : ndaText.Trim();
            if (ndaRequired && nda == null)
            {
                throw new InvalidValueException("thieu_van_ban_nda", "Bật NDA thì phải có văn bản thoả thuận.");
            }

            if (nda != null && nda.Length > CotDb.VanBanNda)
            {
                throw new InvalidValueException("nda_qua_dai", "Văn bản NDA tối đa " + CotDb.VanBanNda + " ký tự.");
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
                    return "Dự án chỉ nhận labeler đã xác minh danh tính (gửi ảnh giấy tờ ở mục Xác minh danh tính).";
                case "chua_du_tuoi":
                    return "Dự án có nội dung nhạy cảm, chỉ nhận người đã xác minh đủ tuổi.";
                case "can_dong_y_dieu_khoan":
                    return "Bạn cần đọc và đồng ý cảnh báo nội dung / ký NDA của dự án trước.";
                default:
                    return "Không đủ điều kiện bảo vệ của dự án.";
            }
        }

        /// <summary>Linh vuc / ngon ngu cua du an (FL-02). Doi duoc moi luc chua ket thuc — chi anh huong goi y.</summary>
        public void DatNhan(IReadOnlyList<string>? linhVuc, IReadOnlyList<string>? ngonNgu, DateTimeOffset luc)
        {
            if (DaKetThuc())
            {
                throw KhongTheKhi("đổi nhãn chủ đề");
            }

            _domains = ChuanHoaNhan(linhVuc, "Lĩnh vực");
            _languages = ChuanHoaNhan(ngonNgu, "Ngôn ngữ");
            UpdatedAt = luc;
        }

        /// <summary>
        /// Admin gop the chu de (tag.merged): thay ma cu bang ma moi, bo trung. Ap ca du an da ket thuc (lich su
        /// van hien dung the). true = co doi.
        /// </summary>
        public bool DoiMaThe(string loai, string maCu, string maMoi)
        {
            if (loai == "domain" && _domains.Contains(maCu))
            {
                _domains = ThayMa(_domains, maCu, maMoi);
                return true;
            }

            if (loai == "language" && _languages.Contains(maCu))
            {
                _languages = ThayMa(_languages, maCu, maMoi);
                return true;
            }

            return false;
        }

        private static List<string> ThayMa(List<string> ds, string maCu, string maMoi)
        {
            List<string> kq = new List<string>();
            foreach (string ma in ds)
            {
                string v = ma == maCu ? maMoi : ma;
                if (!kq.Contains(v))
                {
                    kq.Add(v);
                }
            }

            return kq;
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
                    throw new InvalidValueException("nhan_khong_hop_le", ten + ": mỗi mục từ 1 đến 50 ký tự.");
                }

                if (daCo.Add(v))
                {
                    kq.Add(v);
                }
            }

            if (kq.Count > 10)
            {
                throw new InvalidValueException("nhan_khong_hop_le", ten + ": tối đa 10 mục.");
            }

            return kq;
        }

        /// <summary>Bat / tat dong y xoa file sau khi dong. Da xoa roi thi khong doi duoc nua.</summary>
        public void DatXoaFileSauKhiDong(bool dongY, DateTimeOffset luc)
        {
            if (FilesPurgedAt.HasValue)
            {
                throw new RuleViolationException("file_da_xoa", "File của dự án đã bị xoá.");
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
            ChiKhiNhap("nạp thêm dữ liệu");
        }

        /// <summary>
        /// Cau hoi vang CHI sua duoc khi Nhap: moi bai lam deu cham theo cung bo cau vang — doi giua
        /// chung thi nguoi lam truoc va nguoi lam sau bi cham theo hai luat khac nhau.
        /// </summary>
        public void KiemTraCoTheSuaCauHoiVang()
        {
            if (Status != ProjectStatus.Draft)
            {
                throw KhongTheKhi("sửa câu hỏi vàng");
            }

            if (LabelSchema == null)
            {
                throw new RuleViolationException(
                    "chua_co_tap_nhan",
                    "Phải định nghĩa tập nhãn trước khi tạo câu hỏi vàng.");
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
        /// <param name="soVideoDangChuanBi">File video media-svc chua chuan bi xong (Sample.MediaState) — chan publish.</param>
        public IReadOnlyList<string> NhungGiConThieu(
            int soCauThat, int soCauVangChoTest, bool coDuLieuDangXuLy, QuyDinhDuAn quyDinh,
            IReadOnlyCollection<string>? congCuChoCongLink, DateTimeOffset luc, int soVideoDangChuanBi = 0)
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

            if (soVideoDangChuanBi > 0)
            {
                thieu.Add("video_dang_chuan_bi");
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

        /// <summary>
        /// Canh bao KHONG chan publish:
        ///   - tap nhan co cau tra loi mo (freeText) → "cham_cau_tra_loi_mo_lau": khi du an ket thuc, quality-svc cho
        ///     ml-svc phan tich nghia tung cau roi moi cham (kien truc 3.19), tien cong cua labeler co muon hon;
        ///   - co file video media-svc khong chuan bi duoc (file hong) → "video_khong_xu_ly_duoc". Khong chan: chua co
        ///     cach xoa tung mau, chan thi doanh nghiep ket — admin xem canh bao roi quyet duyet / tu choi.
        /// Thieu cau vang kiem tra KHONG con canh bao (10/2026): cau vang la tuy chon, doanh nghiep khong can bi nhac.
        /// </summary>
        public static IReadOnlyList<string> CanhBao(LabelSchema? tapNhan, int soVideoLoi)
        {
            List<string> ds = new List<string>();
            if (tapNhan != null && tapNhan.Tools.Any(t => t.Kind == ToolKinds.FreeText))
            {
                ds.Add("cham_cau_tra_loi_mo_lau");
            }

            if (soVideoLoi > 0)
            {
                ds.Add("video_khong_xu_ly_duoc");
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
            IReadOnlyCollection<string>? congCuChoCongLink, DateTimeOffset luc, int soVideoDangChuanBi = 0)
        {
            ChiKhiNhap("publish");

            if (phanTramPhi < 0 || phanTramPhi > 100)
            {
                throw new InvalidValueException("phi_khong_hop_le", "Phí nền tảng phải từ 0 đến 100%.");
            }

            IReadOnlyList<string> thieu = NhungGiConThieu(soCauThat, soCauVangChoTest, coDuLieuDangXuLy, quyDinh, congCuChoCongLink, luc, soVideoDangChuanBi);
            if (thieu.Count > 0)
            {
                throw new RuleViolationException(
                    "chua_san_sang_publish",
                    "Chưa thể publish, còn thiếu: " + string.Join(", ", thieu));
            }

            PlatformFeePercent = phanTramPhi;
            QuestionCount = soCauThat;
            DoiTrangThai(ProjectStatus.PendingEscrow, null, luc);
        }

        /// <summary>Ledger da giu tien (escrow.reserved): Cho ky quy → Cho duyet.</summary>
        public void XacNhanDaKyQuy(DateTimeOffset luc)
        {
            ChiKhi(ProjectStatus.PendingEscrow, "xác nhận ký quỹ");

            WasEscrowed = true;
            SubmittedForApprovalAt = luc;
            DoiTrangThai(ProjectStatus.PendingApproval, null, luc);
        }

        /// <summary>Ledger tu choi (escrow.rejected): ve Nhap kem ly do de doanh nghiep sua.</summary>
        public void KyQuyBiTuChoi(string lyDo, DateTimeOffset luc)
        {
            ChiKhi(ProjectStatus.PendingEscrow, "từ chối ký quỹ");
            DoiTrangThai(ProjectStatus.Draft, lyDo, luc);
        }

        /// <summary>Admin duyet (FM-02): Cho duyet → Dang chay.</summary>
        public void Duyet(DateTimeOffset luc)
        {
            ChiKhi(ProjectStatus.PendingApproval, "duyệt");

            if (Deadline.HasValue && Deadline.Value <= luc)
            {
                throw new RuleViolationException(
                    "deadline_da_qua",
                    "Hạn chót đã qua trong lúc chờ duyệt — nên từ chối để hoàn ký quỹ.");
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
            ChiKhi(ProjectStatus.PendingApproval, "từ chối duyệt");
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
                    "Dự án chưa quá hạn chờ duyệt.");
            }

            ClosedAt = luc;
            DoiTrangThai(ProjectStatus.Cancelled, "Qua han cho duyet, tu dong huy va hoan ky quy.", luc);
        }

        public void TamDung(DateTimeOffset luc)
        {
            ChiKhi(ProjectStatus.Running, "tạm dừng");
            DoiTrangThai(ProjectStatus.Paused, null, luc);
        }

        public void TiepTuc(DateTimeOffset luc)
        {
            ChiKhi(ProjectStatus.Paused, "tiếp tục");

            if (Deadline.HasValue && Deadline.Value <= luc)
            {
                throw new RuleViolationException(
                    "deadline_da_qua",
                    "Hạn chót đã qua — hãy hoàn thành dự án thay vì chạy tiếp.");
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
                throw KhongTheKhi("kết thúc sớm");
            }

            if (FinishRequestedAt.HasValue)
            {
                throw new RuleViolationException("da_yeu_cau_ket_thuc", "Đã yêu cầu kết thúc, hệ thống đang đóng thu bài.");
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
                throw KhongTheKhi("bắt đầu chấm");
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

            ChiKhi(ProjectStatus.Grading, "hoàn thành");
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
                throw CanTamDungTruoc("huỷ");
            }

            bool duocHuy = Status == ProjectStatus.Draft
                           || Status == ProjectStatus.PendingApproval
                           || Status == ProjectStatus.Paused;

            if (!duocHuy)
            {
                throw KhongTheKhi("huỷ");
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
                    "Dự án cần chủ dự án duyệt — hãy gửi đơn xin tham gia.");
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
                throw new RuleViolationException("du_an_khong_chay", "Dự án không ở trạng thái nhận người.");
            }

            if (Visibility == ProjectVisibility.Private)
            {
                throw new RuleViolationException("du_an_rieng_tu", "Dự án riêng tư chỉ tham gia qua lời mời.");
            }

            KiemTraKenhChuyenNghiep();
        }

        /// <summary>
        /// Vao bang MA MOI cua chu du an: duoc o moi trang thai chua ket thuc (moi truoc khi
        /// publish cung duoc, nhu moi truc tiep), bo qua che do hien thi va bai test — ma moi
        /// la loi moi cua chu du an.
        /// </summary>
        /// <summary>
        /// Chu du an them thanh vien / tao ma moi: chi khi du an DA DUOC ADMIN DUYET (PublishedAt) va chua ket
        /// thuc — truoc khi duyet du an con co the bi tu choi / sua, moi nguoi vao luc do la vo nghia.
        /// </summary>
        public void KiemTraChoThemThanhVien()
        {
            if (DaKetThuc())
            {
                throw new RuleViolationException("du_an_da_ket_thuc", "Dự án đã kết thúc.");
            }

            if (PublishedAt == null)
            {
                throw new RuleViolationException("du_an_chua_duoc_duyet", "Dự án chưa được admin duyệt — chưa thêm thành viên hay tạo mã mời được.");
            }
        }

        public void KiemTraChoVaoBangMaMoi()
        {
            if (DaKetThuc())
            {
                throw new RuleViolationException("du_an_da_ket_thuc", "Dự án đã kết thúc.");
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
                    return "Tài khoản đang bị khoá.";
                case "khong_phai_labeler":
                    return "Tài khoản không có vai trò labeler.";
                case "chua_du_cap_do":
                    return "Dự án yêu cầu cấp độ tối thiểu " + MinLevel + ", hiện tại là " + hoSo.CapDo + ".";
                case "chua_du_uy_tin":
                    return hoSo.UyTin.HasValue
                        ? "Dự án yêu cầu uy tín tối thiểu " + MinReputation + ", hiện tại là " + hoSo.UyTin.Value + "."
                        : "Dự án yêu cầu uy tín tối thiểu " + MinReputation + ", tài khoản chưa có điểm uy tín.";
                default:
                    return "Không đủ điều kiện tham gia dự án.";
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
                    "Tên dự án phải từ 1 đến " + quyDinh.DoDaiTenToiDa + " ký tự.");
            }

            string moTa = description == null ? string.Empty : description.Trim();
            if (moTa.Length > quyDinh.DoDaiMoTaToiDa)
            {
                throw new InvalidValueException(
                    "mo_ta_qua_dai",
                    "Mô tả tối đa " + quyDinh.DoDaiMoTaToiDa + " ký tự.");
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
                    "Dự án không nhận labeler chuyên nghiệp.");
            }
        }

        private static RuleViolationException CanTamDungTruoc(string hanhDong)
        {
            return new RuleViolationException(
                "can_tam_dung_truoc",
                "Hãy tạm dừng dự án trước khi " + hanhDong + ": ngừng nhận người làm và nộp bài mới.");
        }

        /// <summary>Ten trang thai cho thong bao loi (tieng Viet, khop nhan tren frontend).</summary>
        public static string TenTrangThai(ProjectStatus s)
        {
            switch (s)
            {
                case ProjectStatus.Draft:
                    return "Nháp";
                case ProjectStatus.PendingEscrow:
                    return "Chờ ký quỹ";
                case ProjectStatus.PendingApproval:
                    return "Chờ duyệt";
                case ProjectStatus.Running:
                    return "Đang chạy";
                case ProjectStatus.Paused:
                    return "Tạm dừng";
                case ProjectStatus.Grading:
                    return "Đang chấm";
                case ProjectStatus.Completed:
                    return "Hoàn thành";
                case ProjectStatus.Cancelled:
                    return "Đã huỷ";
                default:
                    return s.ToString();
            }
        }

        private RuleViolationException KhongTheKhi(string hanhDong)
        {
            return new RuleViolationException(
                "chuyen_trang_thai_khong_hop_le",
                "Không thể " + hanhDong + " khi dự án đang ở trạng thái “" + TenTrangThai(Status) + "”.");
        }

        private static void BatBuocCoLyDo(string lyDo)
        {
            if (string.IsNullOrWhiteSpace(lyDo))
            {
                throw new InvalidValueException("thieu_ly_do", "Phải ghi lý do.");
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
