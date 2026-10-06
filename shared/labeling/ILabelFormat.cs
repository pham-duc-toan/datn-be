using System.Collections.Generic;
using System.Text.Json;

namespace Crowd.Labeling
{
    /// <summary>
    /// Mot DINH DANG nhan: hinh dang JSON cua mot loai nhan o mot phien ban.
    ///
    /// Moi loai nhan (phan loai anh, bounding box, NER...) la mot lop cai
    /// interface nay. Them loai moi = viet them mot lop + dang ky o LabelFormats.
    /// Doi hinh dang JSON cua loai da co = viet lop moi voi SchemaVersion + 1;
    /// lop cu giu nguyen de doc duoc nhan cu trong database.
    /// </summary>
    public interface ILabelFormat
    {
        /// <summary>Loai nhan, trung voi gia tri JSON cua enum loai bai toan, vd "imageClassification".</summary>
        string TaskType { get; }

        int SchemaVersion { get; }

        /// <summary>
        /// Kiem cau truc va tra ve JSON CHUAN HOA (bo khoang trang, sap xep
        /// nhung thu khong co thu tu) — hai nhan cung y nghia cho ra cung chuoi.
        /// Sai thi nem LabelFormatException.
        /// </summary>
        string ChuanHoa(JsonElement data);

        /// <summary>
        /// Cac lop nhan xuat hien trong nhan — de kiem voi tap nhan cua du an.
        /// Phan loai: chinh la cac lop da chon. Bounding box: lop cua tung khung.
        /// </summary>
        IReadOnlyList<string> CacLop(JsonElement data);

        /// <summary>Nhan nop co khop dap an khong — cham cau hoi vang / bai test dau vao.</summary>
        bool KhopDapAn(JsonElement nop, JsonElement dapAn);
    }
}
