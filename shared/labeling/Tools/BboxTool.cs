using System.Text.Json.Nodes;

namespace Crowd.Labeling.Tools
{
    /// <summary>
    /// Khung chu nhat tren anh: [{"labelId":"xe","x":10,"y":20,"w":100,"h":50}],
    /// toa do pixel, goc tren-trai. Khung phai nam trong anh (width x height).
    /// Cham dap an: IoU tung cap &gt;= nguong (mac dinh 0.5 — muc pho bien cua COCO/PASCAL VOC).
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

        protected override double DoGiong(JsonObject a, JsonObject b)
        {
            return HinhHoc.IouHop(
                HinhHoc.So(a["x"]), HinhHoc.So(a["y"]), HinhHoc.So(a["w"]), HinhHoc.So(a["h"]),
                HinhHoc.So(b["x"]), HinhHoc.So(b["y"]), HinhHoc.So(b["w"]), HinhHoc.So(b["h"]));
        }

        protected override string KhoaSapXep(JsonObject p)
        {
            return p["labelId"]!.GetValue<string>() + "|" + SoDeSapXep(HinhHoc.So(p["x"])) + "|" + SoDeSapXep(HinhHoc.So(p["y"]))
                   + "|" + SoDeSapXep(HinhHoc.So(p["w"])) + "|" + SoDeSapXep(HinhHoc.So(p["h"]));
        }
    }
}
