using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Crowd.Labeling.Tools
{
    /// <summary>
    /// Khung chu nhat tren anh: [{"labelId":"xe","x":10,"y":20,"w":100,"h":50}],
    /// toa do pixel, goc tren-trai. Khung phai nam trong anh (width x height).
    /// Cham dap an: IoU tung cap &gt;= nguong (mac dinh 0.5 — muc pho bien cua COCO/PASCAL VOC).
    /// Gop nhieu nguoi: ghep cum theo IoU, khung dai dien = TRUNG VI tung toa do.
    /// </summary>
    internal sealed class BboxTool : DanhSachToolBase
    {
        public override string Kind
        {
            get { return ToolKinds.Bbox; }
        }

        protected override double NguongMacDinh
        {
            get { return 0.5; }
        }

        protected override void KiemPhanTu(JsonObject p, ToolDefinition tool, SampleMetadata mau)
        {
            double x = HinhHoc.So(p["x"]);
            double y = HinhHoc.So(p["y"]);
            double w = HinhHoc.So(p["w"]);
            double h = HinhHoc.So(p["h"]);

            if (mau.Width.HasValue && x + w > mau.Width.Value + HinhHoc.SaiSo)
            {
                throw Sai(tool, "khung vuot qua chieu rong anh (" + mau.Width.Value + "px).");
            }

            if (mau.Height.HasValue && y + h > mau.Height.Value + HinhHoc.SaiSo)
            {
                throw Sai(tool, "khung vuot qua chieu cao anh (" + mau.Height.Value + "px).");
            }
        }

        protected override object PhanTich(JsonObject phanTu)
        {
            return new double[] { HinhHoc.So(phanTu["x"]), HinhHoc.So(phanTu["y"]), HinhHoc.So(phanTu["w"]), HinhHoc.So(phanTu["h"]) };
        }

        protected override double DoGiongDaPhanTich(object a, object b, double tyLe)
        {
            double[] k1 = (double[])a;
            double[] k2 = (double[])b;
            return HinhHoc.IouHop(k1[0], k1[1], k1[2], k1[3], k2[0], k2[1], k2[2], k2[3]);
        }

        protected override double DoGiong(JsonObject a, JsonObject b, double tyLe)
        {
            return DoGiongDaPhanTich(PhanTich(a), PhanTich(b), tyLe);
        }

        protected override JsonObject DaiDien(IReadOnlyList<JsonObject> thanhVien, BoSoSanh so)
        {
            return new JsonObject
            {
                ["x"] = HinhHoc.LamTron(HinhHoc.TrungVi(thanhVien.Select(m => HinhHoc.So(m["x"])))),
                ["y"] = HinhHoc.LamTron(HinhHoc.TrungVi(thanhVien.Select(m => HinhHoc.So(m["y"])))),
                ["w"] = HinhHoc.LamTron(HinhHoc.TrungVi(thanhVien.Select(m => HinhHoc.So(m["w"])))),
                ["h"] = HinhHoc.LamTron(HinhHoc.TrungVi(thanhVien.Select(m => HinhHoc.So(m["h"])))),
            };
        }

        protected override string KhoaSapXep(JsonObject p)
        {
            return p["labelId"]!.GetValue<string>() + "|" + SoDeSapXep(HinhHoc.So(p["x"])) + "|" + SoDeSapXep(HinhHoc.So(p["y"]))
                   + "|" + SoDeSapXep(HinhHoc.So(p["w"])) + "|" + SoDeSapXep(HinhHoc.So(p["h"]));
        }
    }
}
