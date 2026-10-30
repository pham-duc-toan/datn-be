using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Crowd.Labeling.Tools
{
    /// <summary>
    /// Mot SO cho ca mau: {"value":4} — thang diem (cham cau tra loi 1..5), so dem (bao nhieu nguoi
    /// trong anh), do dai... Cau hinh: min / max (gioi han), integer (chi so nguyen), tolerance
    /// (lech bao nhieu van tinh la khop; mac dinh 0).
    ///
    /// Cham dap an: |nop − dap an| &lt;= tolerance.
    /// Gop nhieu nguoi: TRUNG VI (khong bi mot nguoi cham lech keo di nhu trung binh); mot nguoi
    /// dong y khi lech trung vi khong qua tolerance; khong qua mot nua dong y → tranh chap.
    /// </summary>
    internal sealed class NumberTool : IToolKind
    {
        public string Kind
        {
            get { return ToolKinds.Number; }
        }

        public JsonNode KiemVaChuanHoa(JsonNode ketQua, ToolDefinition tool, SampleMetadata mau)
        {
            double v = DocSo(ketQua);

            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                throw Sai(tool, "giá trị không phải số hợp lệ.");
            }

            if (tool.IntegerOnly && v != Math.Floor(v))
            {
                throw Sai(tool, "phải là số nguyên.");
            }

            if (tool.Min.HasValue && v < tool.Min.Value)
            {
                throw Sai(tool, "giá trị " + v + " nhỏ hơn mức tối thiểu " + tool.Min.Value + ".");
            }

            if (tool.Max.HasValue && v > tool.Max.Value)
            {
                throw Sai(tool, "giá trị " + v + " lớn hơn mức tối đa " + tool.Max.Value + ".");
            }

            return TaoKetQua(v, tool);
        }

        public bool Khop(JsonNode nop, JsonNode dapAn, ToolDefinition tool, double? nguongMacDinh, SampleMetadata mau)
        {
            return Math.Abs(DocSo(nop) - DocSo(dapAn)) <= tool.Tolerance + HinhHoc.SaiSoNguong;
        }

        public JsonObject Gop(IReadOnlyList<JsonNode> ketQua, ToolDefinition tool, ThamSoGop thamSo)
        {
            List<double> giaTri = new List<double>();
            foreach (JsonNode k in ketQua)
            {
                giaTri.Add(DocSo(k));
            }

            double giua = HinhHoc.TrungVi(giaTri);
            giua = tool.IntegerOnly ? Math.Floor(giua + 0.5) : HinhHoc.LamTron(giua);

            JsonArray dongY = new JsonArray();
            int soDongY = 0;
            foreach (double v in giaTri)
            {
                bool khop = Math.Abs(v - giua) <= tool.Tolerance + HinhHoc.SaiSoNguong;
                dongY.Add(khop);
                if (khop)
                {
                    soDongY++;
                }
            }

            bool tranhChap = soDongY * 2 <= giaTri.Count;
            return new JsonObject
            {
                ["method"] = "median",
                ["final"] = tranhChap ? null : TaoKetQua(giua, tool),
                ["agrees"] = dongY,
                ["disputed"] = tranhChap,
            };
        }

        private static JsonObject TaoKetQua(double v, ToolDefinition tool)
        {
            JsonObject o = new JsonObject();
            if (tool.IntegerOnly)
            {
                o["value"] = (long)v;
            }
            else
            {
                o["value"] = v;
            }

            return o;
        }

        private static double DocSo(JsonNode ketQua)
        {
            return HinhHoc.So(ketQua["value"]);
        }

        private static LabelFormatException Sai(ToolDefinition tool, string loi)
        {
            return new LabelFormatException("nhan_khong_hop_le", TenHienThi.TienToNhan(tool) + loi);
        }
    }
}
