using System;
using Crowd.Project.Domain.Collections;
using Crowd.Project.Domain.Members;
using Crowd.Project.Domain.Projects;

namespace Crowd.Project.Domain.Eligibility
{
    /// <summary>
    /// "Labeler nay co duoc lam bai (bat dau / nop) cua du an nay khong" — MOT ham thuan, test duoc khong can
    /// database.
    ///
    /// Du an, dot thu bai va thanh vien doc thang tu bang GOC (cung database); ho so nguoi dung (khoa tai
    /// khoan, cap do, uy tin) hoi identity-svc qua gRPC ngay luc mo bai (kien truc 3.24 — bo ban sao
    /// labeler_cache). Ca hai luon moi: vua bi khoa / vua tut uy tin la bi chan ngay, khong con khoang "ban sao
    /// chua kip cap nhat". Dieu kien cap do / uy tin dung CHUNG ham voi luc moi / xin vao du an
    /// (LabelingProject.LyDoLabelerKhongDuDieuKien).
    /// </summary>
    public static class EligibilityPolicy
    {
        /// <summary>Tra ve MA ly do tu choi, hoac null neu duoc phep.</summary>
        /// <param name="dot">Dot thu bai — null khi du an chua duoc duyet.</param>
        /// <param name="thanhVien">Dong project_members cua labeler — null khi khong phai thanh vien (hoac da bi go).</param>
        public static string? LyDoTuChoi(
            LabelingProject duAn,
            CollectionRound? dot,
            ProjectMember? thanhVien,
            HoSoNguoiDung hoSo,
            DateTimeOffset luc)
        {
            if (duAn == null)
            {
                throw new ArgumentNullException(nameof(duAn));
            }

            if (hoSo == null)
            {
                throw new ArgumentNullException(nameof(hoSo));
            }

            if (dot == null)
            {
                return "du_an_chua_san_sang";
            }

            if (dot.DaDongThuBai)
            {
                return "du_an_da_dong_thu_bai";
            }

            if (duAn.Status != ProjectStatus.Running)
            {
                return "du_an_khong_chay";
            }

            if (duAn.Deadline.HasValue && duAn.Deadline.Value <= luc)
            {
                return "da_qua_deadline";
            }

            if (!duAn.AllowProfessional)
            {
                return "khong_mo_kenh_chuyen_nghiep";
            }

            if (thanhVien == null)
            {
                return "khong_phai_thanh_vien";
            }

            if (thanhVien.State == MemberState.Blocked)
            {
                return "bi_chan_khoi_du_an";
            }

            if (thanhVien.Role != MemberRole.Labeler)
            {
                return "khong_phai_labeler";
            }

            return duAn.LyDoLabelerKhongDuDieuKien(hoSo);
        }

        /// <summary>
        /// Cau giai thich cho labeler theo MA ly do cua LyDoTuChoi — noi ro vi sao bi chan thay vi mot cau chung
        /// "chua du dieu kien" (frontend hien uu tien cau tieng Viet cua backend).
        /// </summary>
        public static string GiaiThich(string lyDo, LabelingProject duAn, HoSoNguoiDung hoSo)
        {
            if (duAn == null)
            {
                throw new ArgumentNullException(nameof(duAn));
            }

            switch (lyDo)
            {
                case "du_an_chua_san_sang":
                    return "Dự án chưa mở nhận bài.";
                case "du_an_da_dong_thu_bai":
                    return "Dự án đã ngừng nhận bài (đủ người làm, hết hạn chót hoặc doanh nghiệp đã kết thúc).";
                case "du_an_khong_chay":
                    return duAn.Status == ProjectStatus.Paused ? "Dự án đang tạm dừng." : "Dự án không còn nhận bài.";
                case "da_qua_deadline":
                    return "Dự án đã quá hạn chót nhận bài.";
                case "khong_mo_kenh_chuyen_nghiep":
                    return "Dự án không nhận labeler làm bài trực tiếp.";
                case "khong_phai_thanh_vien":
                    return "Bạn chưa là thành viên dự án này — tham gia dự án trước khi làm bài.";
                case "bi_chan_khoi_du_an":
                    return "Bạn đã bị chặn khỏi dự án này.";
                case "khong_phai_labeler":
                    return "Tài khoản không có vai trò labeler trong dự án này.";
                default:
                    return duAn.GiaiThichKhongDuDieuKien(lyDo, hoSo);
            }
        }
    }
}
