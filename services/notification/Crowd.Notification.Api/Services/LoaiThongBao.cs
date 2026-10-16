using System;
using System.Collections.Generic;
using System.Globalization;
using Crowd.Notification.Api.Entities;

namespace Crowd.Notification.Api.Services
{
    /// <summary>
    /// Danh muc loai thong bao. Trong ung dung luon co; EMAIL theo tuy chon nguoi dung, tru loai
    /// BAT BUOC (anh huong tai khoan / tien da ra khoi vi) — nguoi dung khong tat duoc.
    /// Loai ban nhieu (moi nhan duoc duyet, moi khoan treo giai phong) mac dinh TAT email.
    /// </summary>
    public sealed class LoaiThongBao
    {
        public const string KetQuaKyb = "kyb_ket_qua";
        public const string TaiKhoan = "tai_khoan";
        public const string RutTien = "rut_tien";
        public const string NapTien = "nap_tien";
        public const string DuAn = "du_an";
        public const string NhanTuChoi = "nhan_tu_choi";
        public const string NhanDuyet = "nhan_duyet";
        public const string TienKhaDung = "tien_kha_dung";
        public const string TranhChap = "tranh_chap";
        public const string HeThong = "he_thong";
        public const string CapDo = "cap_do";
        public const string ThanhVien = "thanh_vien";
        public const string DonThamGia = "don_tham_gia";
        public const string BaoMat = "bao_mat";
        public const string XacMinhDanhTinh = "xac_minh_danh_tinh";
        public const string LoiMoi = "loi_moi_du_an";

        public static readonly IReadOnlyList<LoaiThongBao> TatCa = new List<LoaiThongBao>
        {
            new LoaiThongBao(KetQuaKyb, "Kết quả xác minh doanh nghiệp", true, true, false),
            new LoaiThongBao(TaiKhoan, "Khoá / mở khoá tài khoản", true, true, false),
            new LoaiThongBao(RutTien, "Kết quả rút tiền", true, true, false),
            new LoaiThongBao(NapTien, "Nạp tiền thành công", false, true, false),
            new LoaiThongBao(DuAn, "Trạng thái dự án (duyệt, ký quỹ, hoàn tiền)", false, true, false),
            new LoaiThongBao(NhanTuChoi, "Nhãn bị từ chối / kết quả khiếu nại", false, true, false),
            new LoaiThongBao(NhanDuyet, "Nhãn được duyệt", false, false, true),
            new LoaiThongBao(TienKhaDung, "Tiền treo chuyển sang khả dụng", false, false, true),
            new LoaiThongBao(TranhChap, "Tranh chấp nhãn đã duyệt", true, true, false),
            new LoaiThongBao(HeThong, "Thông báo từ quản trị viên", false, true, false),
            new LoaiThongBao(CapDo, "Cấp độ và huy hiệu", false, false, false),
            new LoaiThongBao(ThanhVien, "Được thêm vào dự án / kết quả đơn xin tham gia", false, true, false),
            new LoaiThongBao(DonThamGia, "Đơn xin tham gia dự án mới (chủ dự án)", false, false, false),
            new LoaiThongBao(BaoMat, "Bảo mật tài khoản (bật / tắt xác thực hai lớp)", true, true, false),
            new LoaiThongBao(XacMinhDanhTinh, "Kết quả xác minh danh tính", false, true, false),
            new LoaiThongBao(LoiMoi, "Lời mời tham gia dự án", false, true, false),
        };

        private LoaiThongBao(string ma, string ten, bool batBuoc, bool macDinhEmail, bool gop)
        {
            Ma = ma;
            Ten = ten;
            BatBuoc = batBuoc;
            MacDinhEmail = macDinhEmail;
            Gop = gop;
        }

        public string Ma { get; }

        public string Ten { get; }

        /// <summary>Email luon gui, khong tat duoc.</summary>
        public bool BatBuoc { get; }

        public bool MacDinhEmail { get; }

        /// <summary>
        /// Loai ban nhieu (moi nhan duoc duyet, moi khoan treo — voi sharer la MOI LUOT CLICK): su kien moi
        /// CONG DON vao thong bao cung loai con chua doc, khong tao dong moi (xem ThongBaoWriter.GhiGopAsync).
        /// </summary>
        public bool Gop { get; }

        public static LoaiThongBao? Tim(string ma)
        {
            foreach (LoaiThongBao l in TatCa)
            {
                if (string.Equals(l.Ma, ma, StringComparison.Ordinal))
                {
                    return l;
                }
            }

            return null;
        }

        /// <summary>Co gui email cho loai nay khong, xet tuy chon rieng cua nguoi dung (null = chua dat).</summary>
        public bool GuiEmail(bool? tuyChonNguoiDung)
        {
            if (BatBuoc)
            {
                return true;
            }

            return tuyChonNguoiDung.HasValue ? tuyChonNguoiDung.Value : MacDinhEmail;
        }
    }

    /// <summary>
    /// Noi dung tung thong bao — ham THUAN (test duoc, khong cham DB). Tieng Viet co dau: day la
    /// chu nguoi dung doc tren man hinh / trong email. Moi ham kem ban tieng Anh (.En, FC-08).
    /// </summary>
    public static class NoiDungThongBao
    {
        public static string Tien(long vnd)
        {
            return vnd.ToString("#,##0", CultureInfo.GetCultureInfo("vi-VN")) + " đ";
        }

        private static string TienEn(long vnd)
        {
            return vnd.ToString("#,##0", CultureInfo.InvariantCulture) + " VND";
        }

        public static NoiDung DoanhNghiepDaXacMinh()
        {
            return new NoiDung("Doanh nghiệp đã được xác minh",
                "Hồ sơ doanh nghiệp của bạn đã được duyệt. Từ giờ bạn có thể publish dự án.", "/kyb/me")
                .En("Business verified", "Your business profile has been approved. You can now publish projects.");
        }

        public static NoiDung DoanhNghiepBiTuChoi(string lyDo)
        {
            return new NoiDung("Hồ sơ doanh nghiệp bị từ chối",
                "Lý do: " + lyDo + ". Hãy sửa hồ sơ và nộp lại.", "/kyb/me")
                .En("Business verification rejected", "Reason: " + lyDo + ". Please update your profile and resubmit.");
        }

        public static NoiDung TaiKhoanBiKhoa(string? lyDo)
        {
            bool coLyDo = !string.IsNullOrWhiteSpace(lyDo);
            return new NoiDung("Tài khoản bị khoá",
                coLyDo ? "Tài khoản của bạn đã bị khoá. Lý do: " + lyDo + "." : "Tài khoản của bạn đã bị quản trị viên khoá.",
                null)
                .En("Account locked", coLyDo ? "Your account has been locked. Reason: " + lyDo + "." : "Your account has been locked by an administrator.");
        }

        public static NoiDung TaiKhoanMoKhoa()
        {
            return new NoiDung("Tài khoản đã được mở khoá", "Bạn có thể đăng nhập và tiếp tục sử dụng hệ thống.", null)
                .En("Account unlocked", "You can sign in and continue using the platform.");
        }

        public static NoiDung NapTienThanhCong(long vnd)
        {
            return new NoiDung("Nạp tiền thành công", "Đã cộng " + Tien(vnd) + " vào ví doanh nghiệp.", "/ledger/me")
                .En("Deposit successful", TienEn(vnd) + " has been added to your business wallet.");
        }

        public static NoiDung DuAnDangChay(Guid projectId)
        {
            return new NoiDung("Dự án đã được duyệt", "Dự án đã được duyệt và đang chạy — labeler bắt đầu nhận task.", "/projects/" + projectId)
                .En("Project approved", "Your project has been approved and is running — labelers can now take tasks.");
        }

        public static NoiDung DuAnThieuKyQuy(Guid projectId, string lyDo)
        {
            return new NoiDung("Dự án chưa publish được", lyDo, "/projects/" + projectId)
                .En("Project could not be published", lyDo);
        }

        public static NoiDung DuAnBiHuy(Guid projectId, string? lyDo)
        {
            bool coLyDo = !string.IsNullOrWhiteSpace(lyDo);
            return new NoiDung("Dự án đã huỷ",
                coLyDo ? "Dự án đã bị huỷ. Lý do: " + lyDo + "." : "Dự án đã bị huỷ.",
                "/projects/" + projectId)
                .En("Project cancelled", coLyDo ? "The project has been cancelled. Reason: " + lyDo + "." : "The project has been cancelled.");
        }

        public static NoiDung HoanKyQuy(Guid projectId, long vnd)
        {
            return new NoiDung("Hoàn ký quỹ", "Đã hoàn " + Tien(vnd) + " ký quỹ chưa dùng về ví doanh nghiệp.", "/projects/" + projectId)
                .En("Escrow refunded", TienEn(vnd) + " of unused escrow has been returned to your business wallet.");
        }

        /// <summary>soNhan &gt; 1: da gop nhieu nhan cung du an (vnd la tong).</summary>
        public static NoiDung NhanDuocDuyet(Guid projectId, long vnd, int soNhan)
        {
            string dau = soNhan > 1 ? soNhan + " nhãn được duyệt, tổng +" : "+";
            string dauEn = soNhan > 1 ? soNhan + " labels approved, total +" : "+";
            return new NoiDung("Nhãn được duyệt", dau + Tien(vnd) + " (đang treo, sẽ chuyển sang khả dụng sau thời gian treo).", "/projects/" + projectId)
                .En("Label approved", dauEn + TienEn(vnd) + " (on hold, becomes available after the holding period).");
        }

        public static NoiDung NhanBiTuChoi(Guid projectId, string lyDo, bool cuoiCung)
        {
            return cuoiCung
                ? new NoiDung("Khiếu nại bị bác", "Nhãn bị từ chối vĩnh viễn. Ghi chú: " + lyDo + ".", "/projects/" + projectId)
                    .En("Appeal rejected", "The label is permanently rejected. Note: " + lyDo + ".")
                : new NoiDung("Nhãn bị từ chối", "Lý do: " + lyDo + ". Bạn có thể khiếu nại trong thời hạn cho phép.", "/projects/" + projectId)
                    .En("Label rejected", "Reason: " + lyDo + ". You may appeal within the allowed period.");
        }

        /// <summary>soKhoan &gt; 1: da gop nhieu khoan treo (vnd la tong).</summary>
        public static NoiDung TienKhaDung(long vnd, int soKhoan)
        {
            string sau = soKhoan > 1 ? " (" + soKhoan + " khoản)" : string.Empty;
            string sauEn = soKhoan > 1 ? " (" + soKhoan + " items)" : string.Empty;
            return new NoiDung("Tiền đã khả dụng", Tien(vnd) + sau + " đã chuyển sang số dư khả dụng — có thể rút.", "/ledger/me")
                .En("Funds available", TienEn(vnd) + sauEn + " moved to your available balance — ready to withdraw.");
        }

        public static NoiDung RutThanhCong(long vnd)
        {
            return new NoiDung("Rút tiền thành công", "Đã chuyển " + Tien(vnd) + " về tài khoản ngân hàng của bạn.", "/ledger/withdrawals")
                .En("Withdrawal completed", TienEn(vnd) + " has been transferred to your bank account.");
        }

        public static NoiDung RutThatBai(string lyDo)
        {
            return new NoiDung("Rút tiền thất bại", "Lý do: " + lyDo + ". Tiền đã được trả lại ví khả dụng.", "/ledger/withdrawals")
                .En("Withdrawal failed", "Reason: " + lyDo + ". The money has been returned to your available balance.");
        }

        public static NoiDung RutBiTuChoi(long vnd, string lyDo)
        {
            return new NoiDung("Lệnh rút bị từ chối",
                "Lệnh rút " + Tien(vnd) + " bị quản trị viên từ chối. Lý do: " + lyDo + ". Tiền đã về lại ví.", "/ledger/withdrawals")
                .En("Withdrawal rejected", "Your withdrawal of " + TienEn(vnd) + " was rejected by an administrator. Reason: " + lyDo + ". The money is back in your wallet.");
        }

        // ---- tranh chap (FM-06) ----

        public static NoiDung TranhChapMo(Guid projectId, long vnd, string lyDo)
        {
            return new NoiDung("Nhãn đã duyệt bị tranh chấp",
                "Doanh nghiệp phản đối một nhãn đã duyệt của bạn. Khoản " + Tien(vnd) + " đang được giữ lại chờ quản trị viên phân xử. Lý do: " + lyDo + ".",
                "/projects/" + projectId)
                .En("Approved label disputed",
                    "The business disputed one of your approved labels. " + TienEn(vnd) + " is held until an administrator decides. Reason: " + lyDo + ".");
        }

        public static NoiDung TranhChapKetQua(Guid projectId, long vnd, bool laLabeler, bool doanhNghiepThang, string ghiChu)
        {
            string ketCuc;
            string ketCucEn;
            if (doanhNghiepThang)
            {
                ketCuc = laLabeler
                    ? "Quản trị viên xử doanh nghiệp đúng: nhãn bị loại, khoản " + Tien(vnd) + " không được trả."
                    : "Quản trị viên xử bạn đúng: nhãn bị loại, " + Tien(vnd) + " cùng phí nền tảng được hoàn lại.";
                ketCucEn = laLabeler
                    ? "The administrator sided with the business: the label is removed and " + TienEn(vnd) + " will not be paid."
                    : "The administrator sided with you: the label is removed and " + TienEn(vnd) + " plus the platform fee is refunded.";
            }
            else
            {
                ketCuc = laLabeler
                    ? "Quản trị viên xử bạn đúng: khoản " + Tien(vnd) + " tiếp tục được trả như bình thường."
                    : "Quản trị viên giữ nguyên nhãn: khoản " + Tien(vnd) + " vẫn trả cho labeler.";
                ketCucEn = laLabeler
                    ? "The administrator sided with you: " + TienEn(vnd) + " will be paid as usual."
                    : "The administrator kept the label: " + TienEn(vnd) + " is still paid to the labeler.";
            }

            return new NoiDung("Kết quả tranh chấp", ketCuc + " Ghi chú: " + ghiChu + ".", "/projects/" + projectId)
                .En("Dispute outcome", ketCucEn + " Note: " + ghiChu + ".");
        }

        // ---- cap do, huy hieu (FL-12) ----

        public static NoiDung CapDoMoi(int cap)
        {
            return new NoiDung("Cấp độ mới: " + cap, "Cấp độ của bạn giờ là " + cap + ". Một số dự án yêu cầu cấp độ tối thiểu để nhận việc.", "/me/level")
                .En("New level: " + cap, "You are now level " + cap + ". Some projects require a minimum level to take tasks.");
        }

        public static NoiDung HuyHieuMoi(string ten)
        {
            return new NoiDung("Huy hiệu mới", "Bạn vừa đạt huy hiệu “" + ten + "”.", "/me/level")
                .En("New badge", "You just earned the “" + ten + "” badge.");
        }

        // ---- thanh vien du an: moi, don xin tham gia ----

        public static NoiDung DuocThemVaoDuAn(Guid projectId, string? tenDuAn)
        {
            return new NoiDung("Bạn được mời vào dự án",
                "Chủ dự án đã thêm bạn vào " + TenDuAn(tenDuAn) + ". Bạn có thể bắt đầu nhận task.", "/projects/" + projectId)
                .En("You were added to a project", "The project owner added you to " + TenDuAnEn(tenDuAn) + ". You can start taking tasks.");
        }

        public static NoiDung DonThamGiaMoi(Guid projectId, string tenDuAn, int? diem)
        {
            return new NoiDung("Đơn xin tham gia mới",
                "Có labeler xin tham gia " + TenDuAn(tenDuAn) + (diem.HasValue ? " (bài test đầu vào đạt " + diem.Value + "%)" : string.Empty) + ".",
                "/projects/" + projectId + "/join-requests")
                .En("New join request",
                    "A labeler asked to join " + TenDuAnEn(tenDuAn) + (diem.HasValue ? " (entrance test score " + diem.Value + "%)" : string.Empty) + ".");
        }

        /// <summary>Ly do he thong tu tu choi don qua han (project-svc, join_request_ttl).</summary>
        public const string LyDoHetHan = "het_han";

        public static NoiDung KetQuaDonThamGia(Guid projectId, string tenDuAn, bool duyet, string? lyDo)
        {
            if (duyet)
            {
                return new NoiDung("Đơn xin tham gia được duyệt",
                    "Bạn đã là thành viên " + TenDuAn(tenDuAn) + ". Bạn có thể bắt đầu nhận task.", "/projects/" + projectId)
                    .En("Join request approved", "You are now a member of " + TenDuAnEn(tenDuAn) + ". You can start taking tasks.");
            }

            if (string.Equals(lyDo, LyDoHetHan, StringComparison.Ordinal))
            {
                return new NoiDung("Đơn xin tham gia đã hết hạn",
                    "Chủ " + TenDuAn(tenDuAn) + " chưa xử lý đơn của bạn trong thời hạn nên đơn đã tự đóng. Bạn có thể xin lại.",
                    "/projects/join-requests/mine")
                    .En("Join request expired",
                        "The owner of " + TenDuAnEn(tenDuAn) + " did not review your request in time, so it was closed. You may apply again.");
            }

            bool coLyDo = !string.IsNullOrWhiteSpace(lyDo);
            return new NoiDung("Đơn xin tham gia bị từ chối",
                "Đơn xin tham gia " + TenDuAn(tenDuAn) + " bị từ chối" + (coLyDo ? ". Lý do: " + lyDo + "." : "."),
                "/projects/join-requests/mine")
                .En("Join request rejected", "Your request to join " + TenDuAnEn(tenDuAn) + " was rejected" + (coLyDo ? ". Reason: " + lyDo + "." : "."));
        }

        /// <summary>Loi moi vao du an cho NGUOI DA CO tai khoan (thong bao trong ung dung + email theo tuy chon).</summary>
        public static NoiDung LoiMoiDuAn(Guid projectId, string tenDuAn, bool canDongY, DateTimeOffset hetHan)
        {
            string dongY = canDongY ? " Dự án có nội dung cần bạn đọc và đồng ý điều khoản trước khi nhận." : string.Empty;
            string dongYEn = canDongY ? " The project requires you to read and accept its terms before accepting." : string.Empty;
            return new NoiDung("Lời mời tham gia dự án",
                "Bạn được mời tham gia " + TenDuAn(tenDuAn) + ". Lời mời hết hạn lúc " + GioVietNam(hetHan) + "." + dongY,
                "/projects/invitations")
                .En("Project invitation",
                    "You are invited to join " + TenDuAnEn(tenDuAn) + ". The invitation expires at " + GioUtc(hetHan) + "." + dongYEn);
        }

        /// <summary>Email moi NGUOI CHUA CO tai khoan: dang ky bang dung email nay thi loi moi tu gan vao tai khoan.</summary>
        public static NoiDung EmailLoiMoiNguoiMoi(string tenDuAn, string linkDangKy, DateTimeOffset hetHan)
        {
            return new NoiDung("Lời mời tham gia dự án “" + tenDuAn + "” trên Crowd Label",
                "Xin chào,\n\nBạn được mời tham gia dự án gán nhãn “" + tenDuAn + "” trên Crowd Label.\n"
                + "Đăng ký tài khoản bằng chính địa chỉ email này để nhận lời mời:\n" + linkDangKy + "\n\n"
                + "Lời mời hết hạn lúc " + GioVietNam(hetHan) + ". Nếu bạn không quen dự án này, hãy bỏ qua email.\n\n— Crowd Label",
                null)
                .En("Invitation to join “" + tenDuAn + "” on Crowd Label",
                    "Hello,\n\nYou are invited to join the labeling project “" + tenDuAn + "” on Crowd Label.\n"
                    + "Sign up with this exact email address to receive the invitation:\n" + linkDangKy + "\n\n"
                    + "The invitation expires at " + GioUtc(hetHan) + ". If you don't recognise this project, ignore this email.\n\n— Crowd Label");
        }

        private static string TenDuAn(string? ten)
        {
            return string.IsNullOrWhiteSpace(ten) ? "dự án" : "dự án “" + ten + "”";
        }

        private static string TenDuAnEn(string? ten)
        {
            return string.IsNullOrWhiteSpace(ten) ? "the project" : "the project “" + ten + "”";
        }

        // ---- bao mat tai khoan: 2FA, xac minh danh tinh (FC-04, FP-05) ----

        /// <summary>Bat / tat 2FA — BAT BUOC email: ke chiem tai khoan tat 2FA thi chu that biet ngay.</summary>
        public static NoiDung HaiLopThayDoi(bool bat, string lyDo)
        {
            string ai = string.Equals(lyDo, "admin_reset", StringComparison.Ordinal)
                ? "Quản trị viên đã đặt lại xác thực hai lớp cho tài khoản của bạn."
                : (bat ? "Xác thực hai lớp vừa được BẬT cho tài khoản của bạn." : "Xác thực hai lớp vừa bị TẮT trên tài khoản của bạn.");
            string aiEn = string.Equals(lyDo, "admin_reset", StringComparison.Ordinal)
                ? "An administrator has reset two-factor authentication on your account."
                : (bat ? "Two-factor authentication was just ENABLED on your account." : "Two-factor authentication was just DISABLED on your account.");
            return new NoiDung(bat ? "Đã bật xác thực hai lớp" : "Xác thực hai lớp đã tắt",
                ai + " Nếu không phải bạn, hãy đổi mật khẩu ngay và liên hệ quản trị viên.", "/me/security")
                .En(bat ? "Two-factor authentication enabled" : "Two-factor authentication disabled",
                    aiEn + " If this wasn't you, change your password immediately and contact an administrator.");
        }

        public static NoiDung XacMinhDanhTinhDaNop()
        {
            return new NoiDung("Đã nhận hồ sơ xác minh danh tính",
                "Hồ sơ của bạn đang chờ quản trị viên xem xét. Bạn sẽ nhận thông báo khi có kết quả.", "/me/identity-verification")
                .En("Identity verification received", "Your documents are waiting for review. You will be notified of the result.");
        }

        public static NoiDung XacMinhDanhTinhDuyet()
        {
            return new NoiDung("Danh tính đã được xác minh",
                "Hồ sơ xác minh danh tính của bạn đã được duyệt. Bạn có thể tham gia dự án yêu cầu xác minh.", "/me/identity-verification")
                .En("Identity verified", "Your identity has been verified. You can now join projects that require verification.");
        }

        public static NoiDung XacMinhDanhTinhTuChoi(string lyDo)
        {
            return new NoiDung("Hồ sơ xác minh danh tính bị từ chối",
                "Lý do: " + lyDo + ". Bạn có thể nộp lại hồ sơ.", "/me/identity-verification")
                .En("Identity verification rejected", "Reason: " + lyDo + ". You may resubmit your documents.");
        }

        // ---- thong bao toan he thong (FM-09) ----

        public static NoiDung ThongBaoHeThong(string tieuDe, string noiDung)
        {
            return new NoiDung(tieuDe, noiDung, null);
        }

        // ---- van hanh (VD-D-06) ----

        public static NoiDung EmailCanhBaoDlq(string hangDoi, long soMessage, int nguong)
        {
            return new NoiDung("[Crowd Label] DLQ " + hangDoi + " có " + soMessage + " message",
                "Hàng đợi lỗi " + hangDoi + " đang có " + soMessage + " message (ngưỡng " + nguong + ").\n"
                + "Message vào DLQ là event không xử lý được sau nhiều lần thử — tiền / trạng thái có thể đang đứng.\n"
                + "Xem log service tương ứng, sửa nguyên nhân rồi đưa message lại hàng đợi chính.\n\n— admin-svc",
                null);
        }

        // ---- email giao dich (FC-01, FC-03) ----

        public static string EmailXacThuc(string ten, string link, DateTimeOffset hetHan)
        {
            return ThuXacThuc(ten, link, hetHan).NoiDungChinh;
        }

        public static string EmailDatLaiMatKhau(string ten, string link, DateTimeOffset hetHan)
        {
            return ThuDatLaiMatKhau(ten, link, hetHan).NoiDungChinh;
        }

        public static NoiDung ThuXacThuc(string ten, string link, DateTimeOffset hetHan)
        {
            return new NoiDung("Xác thực email Crowd Label",
                "Xin chào " + ten + ",\n\n"
                + "Bấm vào link dưới đây để xác thực email tài khoản Crowd Label:\n" + link + "\n\n"
                + "Link hết hạn lúc " + GioVietNam(hetHan) + ". Nếu bạn không đăng ký, hãy bỏ qua email này.\n\n"
                + "— Crowd Label",
                null)
                .En("Verify your Crowd Label email",
                    "Hello " + ten + ",\n\n"
                    + "Click the link below to verify the email of your Crowd Label account:\n" + link + "\n\n"
                    + "The link expires at " + GioUtc(hetHan) + ". If you didn't sign up, ignore this email.\n\n"
                    + "— Crowd Label");
        }

        public static NoiDung ThuDatLaiMatKhau(string ten, string link, DateTimeOffset hetHan)
        {
            return new NoiDung("Đặt lại mật khẩu Crowd Label",
                "Xin chào " + ten + ",\n\n"
                + "Bấm vào link dưới đây để đặt mật khẩu mới:\n" + link + "\n\n"
                + "Link dùng một lần, hết hạn lúc " + GioVietNam(hetHan) + ". Đặt lại mật khẩu sẽ đăng xuất mọi thiết bị.\n"
                + "Nếu bạn không yêu cầu, hãy bỏ qua email này — mật khẩu hiện tại vẫn giữ nguyên.\n\n"
                + "— Crowd Label",
                null)
                .En("Reset your Crowd Label password",
                    "Hello " + ten + ",\n\n"
                    + "Click the link below to set a new password:\n" + link + "\n\n"
                    + "The link can be used once and expires at " + GioUtc(hetHan) + ". Resetting your password signs out all devices.\n"
                    + "If you didn't request this, ignore this email — your current password stays the same.\n\n"
                    + "— Crowd Label");
        }

        public static string GioVietNam(DateTimeOffset luc)
        {
            return luc.ToOffset(TimeSpan.FromHours(7)).ToString("HH:mm dd/MM/yyyy", CultureInfo.InvariantCulture) + " (giờ Việt Nam)";
        }

        public static string GioUtc(DateTimeOffset luc)
        {
            return luc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";
        }
    }
}
