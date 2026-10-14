using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Crowd.Labeling.Tools
{
    /// <summary>
    /// Da giac tren anh: [{"labelId":"so_tay","points":[[x,y],[x,y],[x,y],...]}].
    /// Moi diem nam trong anh. Thu tu DIEM giu nguyen (dao thu tu la doi hinh).
    /// Cham dap an: IoU (uoc luong bang luoi diem) &gt;= nguong (mac dinh 0.5).
    /// Gop nhieu nguoi: ghep cum theo IoU, da giac dai dien = da giac "o giua" cua cum (so dinh
    /// moi nguoi mot khac nen khong lay trung binh tung dinh duoc).
    /// </summary>
    internal sealed class PolygonTool : DanhSachToolBase
    {
        public override string Kind
        {
            get { return ToolKinds.Polygon; }
        }

        protected override double NguongMacDinh
        {
            get { return 0.5; }
        }

        protected override void KiemPhanTu(JsonObject p, ToolDefinition tool, SampleMetadata mau)
        {
            foreach (double[] d in Diem(p))
            {
                if (mau.Width.HasValue && d[0] > mau.Width.Value + HinhHoc.SaiSo)
                {
                    throw Sai(tool, "diem (" + d[0] + ", " + d[1] + ") nam ngoai chieu rong anh.");
                }

                if (mau.Height.HasValue && d[1] > mau.Height.Value + HinhHoc.SaiSo)
                {
                    throw Sai(tool, "diem (" + d[0] + ", " + d[1] + ") nam ngoai chieu cao anh.");
                }
            }
        }

        protected override object PhanTich(JsonObject phanTu)
        {
            return Diem(phanTu);
        }

        protected override double DoGiongDaPhanTich(object a, object b, double tyLe)
        {
            List<double[]> p1 = (List<double[]>)a;
            List<double[]> p2 = (List<double[]>)b;

            // Hinh bao khong cham nhau thi IoU chac chan bang 0 — khoi phu luoi 100 x 100.
            if (!HinhHoc.HinhBaoGiaoNhau(p1, p2))
            {
                return 0;
            }

            return HinhHoc.IouDaGiac(p1, p2);
        }

        protected override double DoGiong(JsonObject a, JsonObject b, double tyLe)
        {
            return DoGiongDaPhanTich(Diem(a), Diem(b), tyLe);
        }

        protected override string KhoaSapXep(JsonObject p)
        {
            List<double[]> d = Diem(p);
            return p["labelId"]!.GetValue<string>() + "|" + SoDeSapXep(d[0][0]) + "|" + SoDeSapXep(d[0][1]) + "|" + d.Count;
        }

        internal static List<double[]> Diem(JsonObject p)
        {
            List<double[]> ds = new List<double[]>();
            foreach (JsonNode? n in (JsonArray)p["points"]!)
            {
                JsonArray xy = (JsonArray)n!;
                ds.Add(new double[] { HinhHoc.So(xy[0]), HinhHoc.So(xy[1]) });
            }

            return ds;
        }
    }
}
