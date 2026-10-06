using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Crowd.Labeling.Tools
{
    /// <summary>
    /// Doan van ban (NER): [{"labelId":"TEN_NGUOI","start":0,"end":12}], vi tri
    /// tinh theo KY TU, khoang [start, end) — end khong tinh. Doan nam trong van
    /// ban (0..length) va KHONG chong len nhau.
    ///
    /// Cham dap an: F1 tren cac doan trung KHOP HOAN TOAN (cung lop, cung start,
    /// cung end) &gt;= nguong. Mac dinh 1.0 — phai dung het, dung chuan danh gia NER.
    /// </summary>
    internal sealed class SpanTool : DanhSachToolBase
    {
        public override string Kind
        {
            get { return ToolKinds.Span; }
        }

        protected override double NguongMacDinh
        {
            get { return 1.0; }
        }

        protected override void KiemPhanTu(JsonObject p, ToolDefinition tool, SampleMetadata mau)
        {
            int start = p["start"]!.GetValue<int>();
            int end = p["end"]!.GetValue<int>();

            if (start >= end)
            {
                throw Sai(tool, "doan [" + start + ", " + end + ") rong hoac nguoc.");
            }

            if (mau.Length.HasValue && end > mau.Length.Value)
            {
                throw Sai(tool, "doan [" + start + ", " + end + ") vuot qua do dai van ban (" + mau.Length.Value + ").");
            }
        }

        protected override void KiemCaDanhSach(List<JsonObject> ds, ToolDefinition tool)
        {
            List<JsonObject> theoViTri = ds.OrderBy(p => p["start"]!.GetValue<int>()).ToList();
            for (int i = 1; i < theoViTri.Count; i++)
            {
                if (theoViTri[i]["start"]!.GetValue<int>() < theoViTri[i - 1]["end"]!.GetValue<int>())
                {
                    throw Sai(tool, "hai doan chong len nhau.");
                }
            }
        }

        public override bool Khop(JsonNode nop, JsonNode dapAn, ToolDefinition tool)
        {
            HashSet<string> a = new HashSet<string>(DocDanhSach(nop).Select(Khoa));
            HashSet<string> b = new HashSet<string>(DocDanhSach(dapAn).Select(Khoa));

            if (a.Count == 0 && b.Count == 0)
            {
                return true;
            }

            int dung = a.Count(x => b.Contains(x));
            double f1 = (2.0 * dung) / (a.Count + b.Count);
            double nguong = tool.MatchThreshold ?? NguongMacDinh;
            return f1 >= nguong - 1e-9;
        }

        protected override double DoGiong(JsonObject a, JsonObject b)
        {
            return Khoa(a) == Khoa(b) ? 1 : 0;
        }

        protected override string KhoaSapXep(JsonObject p)
        {
            return SoDeSapXep(p["start"]!.GetValue<int>()) + "|" + p["labelId"]!.GetValue<string>();
        }

        private static string Khoa(JsonObject p)
        {
            return p["labelId"]!.GetValue<string>() + "|" + p["start"]!.GetValue<int>() + "|" + p["end"]!.GetValue<int>();
        }
    }
}
