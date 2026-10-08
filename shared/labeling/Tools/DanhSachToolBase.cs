using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Crowd.Labeling.Tools
{
    /// <summary>
    /// Khung chung cho cong cu ket qua la DANH SACH doi tuong co lop (bbox,
    /// polygon, span, temporalSegment):
    ///   - so phan tu trong [minItems, maxItems];
    ///   - labelId cua tung phan tu co trong tap lop cua cong cu;
    ///   - kiem rieng tung phan tu (lop con lam);
    ///   - CHUAN HOA: sap xep cac phan tu — danh sach khung la tap hop, thu tu ve
    ///     khong co nghia, nen hai nhan cung cac khung phai ra cung chuoi JSON.
    ///
    /// So khop dap an (mac dinh): so phan tu bang nhau, va ghep tham lam moi phan
    /// tu cua dap an voi phan tu chua ghep CUNG LOP giong nhat; moi cap phai dat
    /// do giong &gt;= nguong.
    /// </summary>
    internal abstract class DanhSachToolBase : IToolKind
    {
        public abstract string Kind { get; }

        /// <summary>Nguong do giong mac dinh khi cong cu khong khai matchThreshold.</summary>
        protected abstract double NguongMacDinh { get; }

        /// <summary>Kiem mot phan tu theo thong tin mau. Sai thi nem qua Sai(...).</summary>
        protected abstract void KiemPhanTu(JsonObject phanTu, ToolDefinition tool, SampleMetadata mau);

        /// <summary>Do giong 0..1 cua hai phan tu CUNG LOP (IoU...).</summary>
        protected abstract double DoGiong(JsonObject a, JsonObject b);

        /// <summary>Khoa sap xep de chuan hoa.</summary>
        protected abstract string KhoaSapXep(JsonObject phanTu);

        public JsonNode KiemVaChuanHoa(JsonNode ketQua, ToolDefinition tool, SampleMetadata mau)
        {
            List<JsonObject> ds = DocDanhSach(ketQua);

            if (ds.Count < tool.MinItems || ds.Count > tool.MaxItems)
            {
                throw Sai(tool, "so doi tuong phai tu " + tool.MinItems + " den " + tool.MaxItems + " (dang co " + ds.Count + ").");
            }

            foreach (JsonObject p in ds)
            {
                string lop = p["labelId"]!.GetValue<string>();
                if (!tool.CoLop(lop))
                {
                    throw Sai(tool, "lop '" + lop + "' khong co trong tap nhan.");
                }

                KiemPhanTu(p, tool, mau);
            }

            KiemCaDanhSach(ds, tool);

            JsonArray chuan = new JsonArray();
            foreach (JsonObject p in ds.OrderBy(KhoaSapXep, StringComparer.Ordinal))
            {
                chuan.Add(p.DeepClone());
            }

            return chuan;
        }

        /// <summary>Luat tren CA danh sach (vd span khong duoc chong nhau). Mac dinh khong co.</summary>
        protected virtual void KiemCaDanhSach(List<JsonObject> ds, ToolDefinition tool)
        {
        }

        public virtual bool Khop(JsonNode nop, JsonNode dapAn, ToolDefinition tool, double? nguongMacDinh)
        {
            List<JsonObject> a = DocDanhSach(nop);
            List<JsonObject> b = DocDanhSach(dapAn);

            if (a.Count != b.Count)
            {
                return false;
            }

            double nguong = tool.MatchThreshold ?? nguongMacDinh ?? NguongMacDinh;
            bool[] daGhep = new bool[a.Count];

            foreach (JsonObject dung in b)
            {
                string lop = dung["labelId"]!.GetValue<string>();
                int tot = -1;
                double giongNhat = -1;

                for (int i = 0; i < a.Count; i++)
                {
                    if (daGhep[i] || a[i]["labelId"]!.GetValue<string>() != lop)
                    {
                        continue;
                    }

                    double g = DoGiong(a[i], dung);
                    if (g > giongNhat)
                    {
                        giongNhat = g;
                        tot = i;
                    }
                }

                if (tot < 0 || giongNhat < nguong)
                {
                    return false;
                }

                daGhep[tot] = true;
            }

            return true;
        }

        /// <summary>Chua gop tu dong — gop khung/doan cua nhieu nguoi (IoU, STAPLE) la viec cua quality-svc.</summary>
        public JsonObject Gop(IReadOnlyList<JsonNode> ketQua, ToolDefinition tool)
        {
            return new JsonObject { ["method"] = "none" };
        }

        protected static List<JsonObject> DocDanhSach(JsonNode ketQua)
        {
            List<JsonObject> ds = new List<JsonObject>();
            foreach (JsonNode? n in (JsonArray)ketQua)
            {
                ds.Add((JsonObject)n!);
            }

            return ds;
        }

        protected static LabelFormatException Sai(ToolDefinition tool, string loi)
        {
            return new LabelFormatException("nhan_khong_hop_le", "Cong cu '" + tool.Name + "': " + loi);
        }

        /// <summary>So dang chuoi co dinh do dai — de sap xep theo so ma van so sanh chuoi.</summary>
        protected static string SoDeSapXep(double so)
        {
            return so.ToString("0000000000.000", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
