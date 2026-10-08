using System;

namespace Crowd.BuildingBlocks.Settings
{
    /// <summary>
    /// Doc gia tri setting HIEN TAI (admin sua luc chay). Moi lan goi la mot lan
    /// doc — dung ngay tai cho can, KHONG luu vao bien song lau (mat thay doi).
    ///
    /// Khoa khong co trong danh muc → nem loi (go sai khoa la loi lap trinh).
    /// Chua dong bo duoc gia tri nao → tra gia tri khoi tao trong SettingCatalog.
    /// </summary>
    public interface ISettings
    {
        bool DungSai(string key);

        int SoNguyen(string key);

        long SoLon(string key);

        double SoThuc(string key);

        TimeSpan ThoiGian(string key);

        string Chuoi(string key);
    }
}
