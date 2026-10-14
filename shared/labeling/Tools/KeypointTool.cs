using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Crowd.Labeling.Tools
{
    /// <summary>
    /// Diem moc tren anh: [{"labelId":"mui","x":120,"y":80}], toa do pixel. Diem phai nam trong anh.
    ///
    /// Do giong hai diem = 1 − (khoang cach / DUONG CHEO ANH), nen nguong khong phu thuoc do phan
    /// giai. Mac dinh 0.98: lech toi da 2% duong cheo (anh 1920x1080 ≈ 44 pixel).
    /// Gop nhieu nguoi: ghep cum theo do giong, diem dai dien = TRUNG VI x va y.
    /// </summary>
    internal sealed class KeypointTool : DanhSachToolBase
    {
        public override string Kind
        {
            get { return ToolKinds.Keypoint; }
        }

        protected override double NguongMacDinh
        {
            get { return 0.98; }
        }

        protected override void KiemPhanTu(JsonObject p, ToolDefinition tool, SampleMetadata mau)
        {
            double x = HinhHoc.So(p["x"]);
            double y = HinhHoc.So(p["y"]);

            if (mau.Width.HasValue && x > mau.Width.Value + HinhHoc.SaiSo)
            {
                throw Sai(tool, "diem (" + x + ", " + y + ") nam ngoai chieu rong anh.");
            }

            if (mau.Height.HasValue && y > mau.Height.Value + HinhHoc.SaiSo)
            {
                throw Sai(tool, "diem (" + x + ", " + y + ") nam ngoai chieu cao anh.");
            }
        }

        protected override double TyLe(SampleMetadata mau, IEnumerable<JsonObject> tatCa)
        {
            return HinhHoc.DuongCheo(mau, tatCa.Select(p => new double[] { HinhHoc.So(p["x"]), HinhHoc.So(p["y"]) }));
        }

        protected override object PhanTich(JsonObject phanTu)
        {
            return new double[] { HinhHoc.So(phanTu["x"]), HinhHoc.So(phanTu["y"]) };
        }

        protected override double DoGiongDaPhanTich(object a, object b, double tyLe)
        {
            double[] d1 = (double[])a;
            double[] d2 = (double[])b;
            return Math.Max(0, 1 - (HinhHoc.KhoangCach(d1[0], d1[1], d2[0], d2[1]) / tyLe));
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
            };
        }

        protected override string KhoaSapXep(JsonObject p)
        {
            return p["labelId"]!.GetValue<string>() + "|" + SoDeSapXep(HinhHoc.So(p["x"])) + "|" + SoDeSapXep(HinhHoc.So(p["y"]));
        }
    }
}
