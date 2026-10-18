using System;

namespace Crowd.Project.Domain.Common
{
    // Gioi han nghiep vu do ADMIN dat luc chay (bang setting cua admin-svc). Domain
    // khong doc setting truc tiep: tang Api doc roi truyen vao — domain van test duoc
    // khong can ha tang. Gioi han chi ap cho thao tac MOI; du lieu da luu doc lai
    // khong kiem lai (xem TuLuuTru).

    /// <summary>Quy dinh khi tao / cau hinh du an.</summary>
    public sealed class QuyDinhDuAn
    {
        public required int DoDaiTenToiDa { get; init; }

        public required int DoDaiMoTaToiDa { get; init; }

        /// <summary>x, y — khoang doanh nghiep duoc chon nguong dat t (project.pass_threshold_min / _max).</summary>
        public required int NguongDatToiThieu { get; init; }

        public required int NguongDatToiDa { get; init; }

        /// <summary>So cau that toi da cua mot du an (project.max_questions).</summary>
        public required int SoCauToiDa { get; init; }

        public required int SoSuatToiThieu { get; init; }

        public required int SoSuatToiDa { get; init; }

        public required int SoCauTestMacDinh { get; init; }

        public required int NguongDauMacDinh { get; init; }

        public required int SoCauTestToiDa { get; init; }

        public required int DoDaiHuongDanToiDa { get; init; }

        public required int SoViDuToiDa { get; init; }

        public required int DoDaiGiaiThichToiDa { get; init; }

        /// <summary>Thuong toi thieu cho mot nguoi dat (FM-04, project.min_reward_vnd). 0 = khong gioi han.</summary>
        public long ThuongToiThieu { get; init; }
    }

    /// <summary>Quy dinh khi nap du lieu.</summary>
    public sealed class QuyDinhDuLieu
    {
        public required int DoDaiTenLoToiDa { get; init; }

        public required int DoDaiLoiToiDa { get; init; }

        public required int DoDaiTenMauToiDa { get; init; }
    }

    /// <summary>Quy dinh moi / nhan nguoi vao du an.</summary>
    public sealed class QuyDinhThanhVien
    {
        /// <summary>So nguoi toi da trong mot lan moi nhieu nguoi (POST /members/batch).</summary>
        public required int SoNguoiMoiToiDa { get; init; }

        /// <summary>Han dung toi da cua mot ma moi.</summary>
        public required TimeSpan HanMaMoiToiDa { get; init; }

        /// <summary>Don bi tu choi: cho bao lau moi duoc gui don moi.</summary>
        public required TimeSpan ChoXinLaiSauTuChoi { get; init; }
    }

    /// <summary>Quy dinh bai test dau vao.</summary>
    public sealed class QuyDinhBaiTest
    {
        public required int SoLanToiDa { get; init; }

        public required TimeSpan ThoiGianLamBai { get; init; }
    }

    /// <summary>
    /// Do rong COT trong database — la luoc do, chi doi bang migration (khong phai
    /// setting). Setting do dai tuong ung co tran khong vuot cac so nay.
    /// </summary>
    public static class CotDb
    {
        public const int TenDuAn = 200;

        public const int MoTaDuAn = 5000;

        public const int TenLo = 200;

        public const int TomTatLoi = 4000;

        public const int TenMau = 500;
        public const int CanhBaoNoiDung = 1000;
        public const int VanBanNda = 20000;
    }
}
