using System;
using System.Collections.Generic;

namespace Crowd.Labeling
{
    /// <summary>
    /// Ten tieng Viet (co dau) cua loai cong cu, loai du lieu va cac truong trong tap nhan —
    /// dung trong thong bao loi tra cho nguoi dung (khoa ky thuat nhu "bbox", "classes" khong co nghia voi ho).
    /// Giu khop voi TOOL_LABEL / MODALITY_LABEL cua frontend.
    /// </summary>
    public static class TenHienThi
    {
        private static readonly Dictionary<string, string> CongCu = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { ToolKinds.Classification, "Phân loại" },
            { ToolKinds.FreeText, "Câu trả lời mở" },
            { ToolKinds.Number, "Số / thang điểm" },
            { ToolKinds.Bbox, "Khung chữ nhật" },
            { ToolKinds.Polygon, "Đa giác" },
            { ToolKinds.Keypoint, "Điểm mốc" },
            { ToolKinds.Polyline, "Đường gấp khúc" },
            { ToolKinds.Mask, "Mặt nạ (cọ tô)" },
            { ToolKinds.Span, "Đoạn văn bản (NER)" },
            { ToolKinds.Relation, "Quan hệ giữa đoạn" },
            { ToolKinds.Transcription, "Chép lại nội dung" },
            { ToolKinds.TemporalSegment, "Đoạn thời gian" },
            { ToolKinds.Track, "Theo dõi đối tượng" },
            { ToolKinds.Pairwise, "So sánh cặp" },
            { ToolKinds.Ranking, "Xếp hạng" },
        };

        private static readonly Dictionary<string, string> LoaiDuLieu = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "image", "ảnh" },
            { "text", "văn bản" },
            { "audio", "âm thanh" },
            { "video", "video" },
            { "pair", "cặp" },
            { "list", "danh sách" },
        };

        /// <summary>Truong cua mot cong cu / thuoc tinh trong tap nhan.</summary>
        private static readonly Dictionary<string, string> Truong = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "modality", "loại dữ liệu" },
            { "segmentSeconds", "độ dài đoạn cắt" },
            { "tools", "danh sách công cụ" },
            { "name", "tên khoá" },
            { "kind", "loại công cụ" },
            { "classes", "danh sách lớp" },
            { "allowMultiple", "tuỳ chọn chọn nhiều lớp" },
            { "required", "tuỳ chọn bắt buộc" },
            { "minItems", "số đối tượng tối thiểu" },
            { "maxItems", "số đối tượng tối đa" },
            { "maxLength", "độ dài tối đa" },
            { "allowTie", "tuỳ chọn “ngang nhau”" },
            { "matchThreshold", "ngưỡng khớp" },
            { "attributes", "danh sách thuộc tính" },
            { "spanTool", "công cụ đoạn gốc" },
            { "min", "giá trị nhỏ nhất" },
            { "max", "giá trị lớn nhất" },
            { "integer", "tuỳ chọn số nguyên" },
            { "tolerance", "độ lệch cho phép" },
            { "rotatable", "tuỳ chọn khung xoay" },
            { "type", "kiểu thuộc tính" },
            { "options", "các lựa chọn" },
        };

        /// <summary>Vd "bbox" -&gt; "Khung chữ nhật". Loai la -&gt; giu nguyen.</summary>
        public static string CuaCongCu(string kind)
        {
            string? ten;
            return kind != null && CongCu.TryGetValue(kind, out ten) ? ten : kind ?? "";
        }

        /// <summary>Vd "image" -&gt; "ảnh".</summary>
        public static string CuaLoaiDuLieu(string modality)
        {
            string? ten;
            return modality != null && LoaiDuLieu.TryGetValue(modality, out ten) ? ten : modality ?? "";
        }

        /// <summary>Vd "classes" -&gt; "danh sách lớp". Truong la -&gt; null.</summary>
        public static string? CuaTruong(string truong)
        {
            string? ten;
            return truong != null && Truong.TryGetValue(truong, out ten) ? ten : null;
        }

        /// <summary>Tien to thong bao loi nhan cua mot cong cu: "Khung chữ nhật: ".</summary>
        public static string TienToNhan(ToolDefinition tool)
        {
            if (tool == null)
            {
                throw new ArgumentNullException(nameof(tool));
            }

            return CuaCongCu(tool.Kind) + ": ";
        }
    }
}
