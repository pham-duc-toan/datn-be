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

        public required int RedundancyToiDa { get; init; }

        public required int GoldCheckPercentMacDinh { get; init; }

        public required int GoldCheckPercentToiDa { get; init; }

        public required int SoCauTestMacDinh { get; init; }

        public required int NguongDauMacDinh { get; init; }

        public required int SoCauTestToiDa { get; init; }

        public required int DoDaiHuongDanToiDa { get; init; }

        public required int SoViDuToiDa { get; init; }

        public required int DoDaiGiaiThichToiDa { get; init; }
    }

    /// <summary>Quy dinh khi nap du lieu.</summary>
    public sealed class QuyDinhDuLieu
    {
        public required int DoDaiTenLoToiDa { get; init; }

        public required int DoDaiLoiToiDa { get; init; }

        public required int DoDaiTenMauToiDa { get; init; }
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
    }
}
