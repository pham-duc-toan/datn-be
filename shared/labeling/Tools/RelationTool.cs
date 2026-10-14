using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Crowd.Labeling.Tools
{
    /// <summary>
    /// Quan he CO HUONG giua hai doan van ban (trich xuat quan he):
    ///     [{"labelId":"lam_viec_tai","from":{"start":0,"end":5},"to":{"start":18,"end":24}}]
    /// Hai dau la VI TRI [start, end) cua hai doan DA CO trong ket qua cong cu span ma "spanTool"
    /// tro toi (LabelPayload.Tao kiem cheo sau khi chuan hoa tung cong cu). Tham chieu bang vi tri
    /// chu khong bang so thu tu: danh sach doan duoc sap xep lai khi chuan hoa, so thu tu se lech.
    ///
    /// Cham dap an: F1 tren cac quan he TRUNG HET (cung lop, cung hai dau) &gt;= nguong (mac dinh 1.0).
    /// Gop nhieu nguoi: giu quan he duoc hon mot nua so nguoi ghi y het.
    /// </summary>
    internal sealed class RelationTool : DanhSachToolBase
    {
        public override string Kind
        {
            get { return ToolKinds.Relation; }
        }

        protected override double NguongMacDinh
        {
            get { return 1.0; }
        }

        protected override string CachGop
        {
            get { return "majority"; }
        }

        protected override double NguongGhep(ToolDefinition tool, double? nguongMacDinh)
        {
            return 1.0;
        }

        protected override void KiemPhanTu(JsonObject p, ToolDefinition tool, SampleMetadata mau)
        {
            KiemDau(p, "from", tool, mau);
            KiemDau(p, "to", tool, mau);

            if (KhoaDau(p, "from") == KhoaDau(p, "to"))
            {
                throw Sai(tool, "quan he phai noi HAI doan khac nhau.");
            }
        }

        protected override void KiemCaDanhSach(List<JsonObject> ds, ToolDefinition tool)
        {
            HashSet<string> daCo = new HashSet<string>();
            foreach (JsonObject p in ds)
            {
                if (!daCo.Add(Khoa(p)))
                {
                    throw Sai(tool, "quan he bi ghi trung.");
                }
            }
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
            return SoDeSapXep(HinhHoc.SoNguyen(p["from"]!["start"])) + "|" + SoDeSapXep(HinhHoc.SoNguyen(p["from"]!["end"]))
                   + "|" + SoDeSapXep(HinhHoc.SoNguyen(p["to"]!["start"])) + "|" + SoDeSapXep(HinhHoc.SoNguyen(p["to"]!["end"]))
                   + "|" + p["labelId"]!.GetValue<string>();
        }

        /// <summary>
        /// Moi dau cua moi quan he phai la mot doan co trong ket qua cong cu span (da chuan hoa).
        /// ketQuaSpan null = nhan khong co cong cu span (cong cu tuy chon bi bo trong).
        /// </summary>
        internal static void KiemThamChieu(JsonArray quanHe, JsonArray? ketQuaSpan, ToolDefinition tool)
        {
            HashSet<string> doan = new HashSet<string>();
            if (ketQuaSpan != null)
            {
                foreach (JsonNode? n in ketQuaSpan)
                {
                    doan.Add(HinhHoc.SoNguyen(n!["start"]) + "-" + HinhHoc.SoNguyen(n["end"]));
                }
            }

            foreach (JsonNode? n in quanHe)
            {
                JsonObject p = (JsonObject)n!;
                foreach (string dau in new string[] { "from", "to" })
                {
                    if (!doan.Contains(KhoaDau(p, dau)))
                    {
                        throw Sai(tool, "dau '" + dau + "' [" + p[dau]!["start"] + ", " + p[dau]!["end"]
                                        + ") khong phai mot doan trong cong cu '" + tool.SpanTool + "'.");
                    }
                }
            }
        }

        private static void KiemDau(JsonObject p, string dau, ToolDefinition tool, SampleMetadata mau)
        {
            JsonObject viTri = (JsonObject)p[dau]!;
            int start = HinhHoc.SoNguyen(viTri["start"]);
            int end = HinhHoc.SoNguyen(viTri["end"]);
            viTri["start"] = start;
            viTri["end"] = end;

            if (start >= end)
            {
                throw Sai(tool, "dau '" + dau + "' [" + start + ", " + end + ") rong hoac nguoc.");
            }

            if (mau.Length.HasValue && end > mau.Length.Value)
            {
                throw Sai(tool, "dau '" + dau + "' [" + start + ", " + end + ") vuot qua do dai van ban (" + mau.Length.Value + ").");
            }
        }

        private static string KhoaDau(JsonObject p, string dau)
        {
            return HinhHoc.SoNguyen(p[dau]!["start"]) + "-" + HinhHoc.SoNguyen(p[dau]!["end"]);
        }

        private static string Khoa(JsonObject p)
        {
            return p["labelId"]!.GetValue<string>() + "|" + KhoaDau(p, "from") + "|" + KhoaDau(p, "to");
        }
    }
}
