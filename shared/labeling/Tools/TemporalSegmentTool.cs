using System.Text.Json.Nodes;

namespace Crowd.Labeling.Tools
{
    /// <summary>
    /// Doan thoi gian trong audio / video: [{"labelId":"noi","start":1.5,"end":4.0}],
    /// don vi GIAY, tinh tu DAU MAU (doan cat), 0 &lt;= start &lt; end &lt;= durationSec.
    /// Cham dap an: IoU thoi gian tung cap &gt;= nguong (mac dinh 0.5).
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

        protected override double DoGiong(JsonObject a, JsonObject b)
        {
            return HinhHoc.IouDoan(HinhHoc.So(a["start"]), HinhHoc.So(a["end"]), HinhHoc.So(b["start"]), HinhHoc.So(b["end"]));
        }

        protected override string KhoaSapXep(JsonObject p)
        {
            return SoDeSapXep(HinhHoc.So(p["start"])) + "|" + p["labelId"]!.GetValue<string>() + "|" + SoDeSapXep(HinhHoc.So(p["end"]));
        }
    }
}
