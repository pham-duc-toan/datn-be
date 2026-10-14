using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Crowd.Labeling.Tools
{
    /// <summary>
    /// Doan thoi gian trong audio / video: [{"labelId":"noi","start":1.5,"end":4.0}],
    /// don vi GIAY, tinh tu DAU MAU (doan cat), 0 &lt;= start &lt; end &lt;= durationSec.
    /// Cham dap an: IoU thoi gian tung cap &gt;= nguong (mac dinh 0.5).
    /// Gop nhieu nguoi: ghep cum theo IoU thoi gian, doan dai dien = TRUNG VI diem dau / diem cuoi.
    /// </summary>
    internal sealed class TemporalSegmentTool : DanhSachToolBase
    {
        public override string Kind
        {
            get { return ToolKinds.TemporalSegment; }
        }

        protected override double NguongMacDinh
        {
            get { return 0.5; }
        }

        protected override void KiemPhanTu(JsonObject p, ToolDefinition tool, SampleMetadata mau)
        {
            double start = HinhHoc.So(p["start"]);
            double end = HinhHoc.So(p["end"]);

            if (start >= end)
            {
                throw Sai(tool, "doan [" + start + ", " + end + "] rong hoac nguoc.");
            }

            if (mau.DurationSec.HasValue && end > mau.DurationSec.Value + HinhHoc.SaiSo)
            {
                throw Sai(tool, "doan ket thuc o " + end + "s, vuot qua do dai mau " + mau.DurationSec.Value + "s.");
            }
        }

        protected override object PhanTich(JsonObject phanTu)
        {
            return new double[] { HinhHoc.So(phanTu["start"]), HinhHoc.So(phanTu["end"]) };
        }

        protected override double DoGiongDaPhanTich(object a, object b, double tyLe)
        {
            double[] d1 = (double[])a;
            double[] d2 = (double[])b;
            return HinhHoc.IouDoan(d1[0], d1[1], d2[0], d2[1]);
        }

        protected override double DoGiong(JsonObject a, JsonObject b, double tyLe)
        {
            return DoGiongDaPhanTich(PhanTich(a), PhanTich(b), tyLe);
        }

        protected override JsonObject DaiDien(IReadOnlyList<JsonObject> thanhVien, BoSoSanh so)
        {
            return new JsonObject
            {
                ["start"] = HinhHoc.LamTron(HinhHoc.TrungVi(thanhVien.Select(m => HinhHoc.So(m["start"])))),
                ["end"] = HinhHoc.LamTron(HinhHoc.TrungVi(thanhVien.Select(m => HinhHoc.So(m["end"])))),
            };
        }

        protected override string KhoaSapXep(JsonObject p)
        {
            return SoDeSapXep(HinhHoc.So(p["start"])) + "|" + p["labelId"]!.GetValue<string>() + "|" + SoDeSapXep(HinhHoc.So(p["end"]));
        }
    }
}
