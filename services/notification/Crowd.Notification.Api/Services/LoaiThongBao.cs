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
    /// chu nguoi dung doc tren man hinh / trong email.
    /// </summary>
    public static class NoiDungThongBao
    {
        public static string Tien(long vnd)
        {
            return vnd.ToString("#,##0", CultureInfo.GetCultureInfo("vi-VN")) + " đ";
        }

        public static NoiDung DoanhNghiepDaXacMinh()
        {
            return new NoiDung("Doanh nghiệp đã được xác minh",
                "Hồ sơ doanh nghiệp của bạn đã được duyệt. Từ giờ bạn có thể publish dự án.", "/kyb/me");
        }

        public static NoiDung DoanhNghiepBiTuChoi(string lyDo)
        {
            return new NoiDung("Hồ sơ doanh nghiệp bị từ chối",
                "Lý do: " + lyDo + ". Hãy sửa hồ sơ và nộp lại.", "/kyb/me");
        }

        public static NoiDung TaiKhoanBiKhoa(string? lyDo)
        {
            return new NoiDung("Tài khoản bị khoá",
                string.IsNullOrWhiteSpace(lyDo) ? "Tài khoản của bạn đã bị quản trị viên khoá." : "Tài khoản của bạn đã bị khoá. Lý do: " + lyDo + ".",
                null);
        }

        public static NoiDung TaiKhoanMoKhoa()
        {
            return new NoiDung("Tài khoản đã được mở khoá", "Bạn có thể đăng nhập và tiếp tục sử dụng hệ thống.", null);
        }

        public static NoiDung NapTienThanhCong(long vnd)
        {
            return new NoiDung("Nạp tiền thành công", "Đã cộng " + Tien(vnd) + " vào ví doanh nghiệp.", "/ledger/me");
        }

        public static NoiDung DuAnDangChay(Guid projectId)
        {
            return new NoiDung("Dự án đã được duyệt", "Dự án đã được duyệt và đang chạy — labeler bắt đầu nhận task.", "/projects/" + projectId);
        }

        public static NoiDung DuAnThieuKyQuy(Guid projectId, string lyDo)
        {
            return new NoiDung("Dự án chưa publish được", lyDo, "/projects/" + projectId);
        }

        public static NoiDung DuAnBiHuy(Guid projectId, string? lyDo)
        {
            return new NoiDung("Dự án đã huỷ",
                string.IsNullOrWhiteSpace(lyDo) ? "Dự án đã bị huỷ." : "Dự án đã bị huỷ. Lý do: " + lyDo + ".",
                "/projects/" + projectId);
        }

        public static NoiDung HoanKyQuy(Guid projectId, long vnd)
        {
            return new NoiDung("Hoàn ký quỹ", "Đã hoàn " + Tien(vnd) + " ký quỹ chưa dùng về ví doanh nghiệp.", "/projects/" + projectId);
        }

        /// <summary>soNhan &gt; 1: da gop nhieu nhan cung du an (vnd la tong).</summary>
        public static NoiDung NhanDuocDuyet(Guid projectId, long vnd, int soNhan)
        {
            string dau = soNhan > 1 ? soNhan + " nhãn được duyệt, tổng +" : "+";
            return new NoiDung("Nhãn được duyệt", dau + Tien(vnd) + " (đang treo, sẽ chuyển sang khả dụng sau thời gian treo).", "/projects/" + projectId);
        }

        public static NoiDung NhanBiTuChoi(Guid projectId, string lyDo, bool cuoiCung)
        {
            return cuoiCung
                ? new NoiDung("Khiếu nại bị bác", "Nhãn bị từ chối vĩnh viễn. Ghi chú: " + lyDo + ".", "/projects/" + projectId)
                : new NoiDung("Nhãn bị từ chối", "Lý do: " + lyDo + ". Bạn có thể khiếu nại trong thời hạn cho phép.", "/projects/" + projectId);
        }

        /// <summary>soKhoan &gt; 1: da gop nhieu khoan treo (vnd la tong).</summary>
        public static NoiDung TienKhaDung(long vnd, int soKhoan)
        {
            string sau = soKhoan > 1 ? " (" + soKhoan + " khoản)" : string.Empty;
            return new NoiDung("Tiền đã khả dụng", Tien(vnd) + sau + " đã chuyển sang số dư khả dụng — có thể rút.", "/ledger/me");
        }

        public static NoiDung RutThanhCong(long vnd)
        {
            return new NoiDung("Rút tiền thành công", "Đã chuyển " + Tien(vnd) + " về tài khoản ngân hàng của bạn.", "/ledger/withdrawals");
        }

        public static NoiDung RutThatBai(string lyDo)
        {
            return new NoiDung("Rút tiền thất bại", "Lý do: " + lyDo + ". Tiền đã được trả lại ví khả dụng.", "/ledger/withdrawals");
        }

        public static NoiDung RutBiTuChoi(long vnd, string lyDo)
        {
            return new NoiDung("Lệnh rút bị từ chối",
                "Lệnh rút " + Tien(vnd) + " bị quản trị viên từ chối. Lý do: " + lyDo + ". Tiền đã về lại ví.", "/ledger/withdrawals");
        }
    }
}
