using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Crowd.Labeling.Tools
{
    /// <summary>
    /// Duong gap khuc KHONG khep kin tren anh (lan duong, day dien, duong vien):
    ///     [{"labelId":"lan","points":[[0,40],[50,42],[100,45]]}]
    /// Moi diem nam trong anh; duong phai co do dai lon hon 0.
    ///
    /// Do giong hai duong = 1 − (do lech trung binh / DUONG CHEO ANH); do lech do bang cach lay
    /// mau deu tren moi duong (HinhHoc.DoLechDuong), khong phu thuoc chieu ve va so dinh.
    /// Mac dinh 0.97: lech trung binh toi da 3% duong cheo.
    /// Gop nhieu nguoi: ghep cum theo do giong, duong dai dien = duong "o giua" cua cum.
    /// </summary>
    internal sealed class PolylineTool : DanhSachToolBase
    {
        public override string Kind
        {
            get { return ToolKinds.Polyline; }
        }

        protected override double NguongMacDinh
        {
            get { return 0.97; }
        }

        protected override void KiemPhanTu(JsonObject p, ToolDefinition tool, SampleMetadata mau)
        {
            List<double[]> diem = PolygonTool.Diem(p);
            foreach (double[] d in diem)
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

            if (HinhHoc.DoDaiDuong(diem) <= 0)
            {
                throw Sai(tool, "duong co do dai bang 0 (cac diem trung nhau).");
            }
        }

        protected override double TyLe(SampleMetadata mau, IEnumerable<JsonObject> tatCa)
        {
            return HinhHoc.DuongCheo(mau, tatCa.SelectMany(p => PolygonTool.Diem(p)));
        }

        /// <summary>Ban da doc = cac diem lay mau deu doc duong (dung lai cho moi lan so).</summary>
        protected override object PhanTich(JsonObject phanTu)
        {
            return HinhHoc.LayMauDuong(PolygonTool.Diem(phanTu), HinhHoc.SoDiemMauDuong);
        }

        protected override double DoGiongDaPhanTich(object a, object b, double tyLe)
        {
            return Math.Max(0, 1 - (HinhHoc.DoLechMauDuong((List<double[]>)a, (List<double[]>)b) / tyLe));
        }

        protected override double DoGiong(JsonObject a, JsonObject b, double tyLe)
        {
            return DoGiongDaPhanTich(PhanTich(a), PhanTich(b), tyLe);
        }

        protected override string KhoaSapXep(JsonObject p)
        {
            List<double[]> d = PolygonTool.Diem(p);
            return p["labelId"]!.GetValue<string>() + "|" + SoDeSapXep(d[0][0]) + "|" + SoDeSapXep(d[0][1]) + "|" + d.Count;
        }
    }
}
