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
    ///
    /// Gop nhieu nguoi: giu doan duoc HON MOT NUA so nguoi danh dau y het (cung lop, cung vi tri).
    /// Hai doan qua ban khong the chong nhau: phai co mot nguoi danh dau ca hai, ma nhan cua moi
    /// nguoi da bi cam chong doan.
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
            int start = HinhHoc.SoNguyen(p["start"]);
            int end = HinhHoc.SoNguyen(p["end"]);

            // Ghi lai dang so nguyen: 5.0 va 5 la mot vi tri, ban chuan phai ra cung mot chuoi.
            p["start"] = start;
            p["end"] = end;

            if (start >= end)
            {
                throw Sai(tool, "đoạn [" + start + ", " + end + ") trống hoặc ngược.");
            }

            if (mau.Length.HasValue && end > mau.Length.Value)
            {
                throw Sai(tool, "đoạn [" + start + ", " + end + ") vượt quá độ dài văn bản (" + mau.Length.Value + ").");
            }
        }

        protected override void KiemCaDanhSach(List<JsonObject> ds, ToolDefinition tool)
        {
            List<JsonObject> theoViTri = ds.OrderBy(p => HinhHoc.SoNguyen(p["start"])).ToList();
            for (int i = 1; i < theoViTri.Count; i++)
            {
                if (HinhHoc.SoNguyen(theoViTri[i]["start"]) < HinhHoc.SoNguyen(theoViTri[i - 1]["end"]))
                {
                    throw Sai(tool, "hai đoạn chồng lên nhau.");
                }
            }
        }

        protected override string CachGop
        {
            get { return "majority"; }
        }

        /// <summary>Ghep cap luon doi TRUNG HET; matchThreshold cua span la nguong F1 khi cham dap an.</summary>
        protected override double NguongGhep(ToolDefinition tool, double? nguongMacDinh)
        {
            return 1.0;
        }

        public override bool Khop(JsonNode nop, JsonNode dapAn, ToolDefinition tool, double? nguongMacDinh, SampleMetadata mau)
        {
            double nguong = tool.MatchThreshold ?? nguongMacDinh ?? NguongMacDinh;
            return DiemF1(DocDanhSach(nop), DocDanhSach(dapAn), tool, 1.0, new BoSoSanh(this, 1)) >= nguong - HinhHoc.SaiSoNguong;
        }

        protected override object PhanTich(JsonObject phanTu)
        {
            return Khoa(phanTu);
        }

        protected override double DoGiongDaPhanTich(object a, object b, double tyLe)
        {
            return string.Equals((string)a, (string)b, System.StringComparison.Ordinal) ? 1 : 0;
        }

        protected override double DoGiong(JsonObject a, JsonObject b, double tyLe)
        {
            return Khoa(a) == Khoa(b) ? 1 : 0;
        }

        protected override string KhoaSapXep(JsonObject p)
        {
            return SoDeSapXep(HinhHoc.SoNguyen(p["start"])) + "|" + p["labelId"]!.GetValue<string>();
        }

        private static string Khoa(JsonObject p)
        {
            return p["labelId"]!.GetValue<string>() + "|" + HinhHoc.SoNguyen(p["start"]) + "|" + HinhHoc.SoNguyen(p["end"]);
        }
    }
}
