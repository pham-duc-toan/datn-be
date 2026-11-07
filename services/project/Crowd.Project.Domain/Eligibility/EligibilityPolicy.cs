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
    }
}
