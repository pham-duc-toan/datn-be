using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Crowd.Labeling.Formats
{
    /// <summary>
    /// Phan loai anh, phien ban 1. Labeler xem anh, chon mot hoac nhieu lop:
    ///
    ///     { "labelIds": ["do"] }
    ///     { "labelIds": ["cho", "meo"] }     ← du an cho chon nhieu lop
    ///
    /// Chon mot hay nhieu lop la luat cua TAP NHAN du an (allowMultiple), khong
    /// phai cua dinh dang — service kiem them bang CacLop().
    ///
    /// Chuan hoa: sap xep labelIds theo thu tu ky tu. ["meo","cho"] va
    /// ["cho","meo"] la CUNG mot nhan nen phai ra cung mot chuoi.
    /// </summary>
    public sealed class ImageClassificationV1 : ILabelFormat
    {
        public const int SoLopToiDa = 100;
        public const int DoDaiTenLopToiDa = 100;

        private const string TruongLabelIds = "labelIds";

        public string TaskType
        {
            get { return LabelTaskTypes.ImageClassification; }
        }

        public int SchemaVersion
        {
            get { return 1; }
        }

        public string ChuanHoa(JsonElement data)
        {
            List<string> lop = DocLop(data);
            lop.Sort(StringComparer.Ordinal);

            Dictionary<string, List<string>> chuan = new Dictionary<string, List<string>>();
            chuan[TruongLabelIds] = lop;

            return JsonSerializer.Serialize(chuan);
        }

        public IReadOnlyList<string> CacLop(JsonElement data)
        {
            return DocLop(data);
        }

        /// <summary>Khop khi chon DUNG TAP lop cua dap an — khong thieu, khong thua.</summary>
        public bool KhopDapAn(JsonElement nop, JsonElement dapAn)
        {
            HashSet<string> a = new HashSet<string>(DocLop(nop), StringComparer.Ordinal);
            HashSet<string> b = new HashSet<string>(DocLop(dapAn), StringComparer.Ordinal);
            return a.SetEquals(b);
        }

        private static List<string> DocLop(JsonElement data)
        {
            if (data.ValueKind != JsonValueKind.Object)
            {
                throw new LabelFormatException("Nhan phan loai phai la mot object { \"labelIds\": [...] }.");
            }

            JsonElement mang = default;
            bool coLabelIds = false;

            foreach (JsonProperty p in data.EnumerateObject())
            {
                // Truong la la loi, khong bo qua: client gui sai ten truong
                // ("labels" thay vi "labelIds") phai biet ngay.
                if (p.Name != TruongLabelIds)
                {
                    throw new LabelFormatException("Nhan phan loai khong co truong '" + p.Name + "'.");
                }

                mang = p.Value;
                coLabelIds = true;
            }

            if (!coLabelIds || mang.ValueKind != JsonValueKind.Array)
            {
                throw new LabelFormatException("Thieu mang \"labelIds\".");
            }

            List<string> lop = new List<string>();
            foreach (JsonElement phanTu in mang.EnumerateArray())
            {
                string? ten = phanTu.ValueKind == JsonValueKind.String ? phanTu.GetString() : null;

                if (string.IsNullOrWhiteSpace(ten) || ten.Length > DoDaiTenLopToiDa)
                {
                    throw new LabelFormatException("Moi phan tu trong labelIds phai la chuoi 1-" + DoDaiTenLopToiDa + " ky tu.");
                }

                lop.Add(ten);
            }

            if (lop.Count == 0 || lop.Count > SoLopToiDa)
            {
                throw new LabelFormatException("labelIds phai co tu 1 den " + SoLopToiDa + " lop.");
            }

            if (lop.Distinct(StringComparer.Ordinal).Count() != lop.Count)
            {
                throw new LabelFormatException("labelIds co lop bi lap lai.");
            }

            return lop;
        }
    }
}
