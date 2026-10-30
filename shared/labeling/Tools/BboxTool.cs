using System;
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
    ///
    /// KHUNG XOAY (cong cu "rotatable": true): them "angle" (do, -90..90, thuan chieu kim dong ho tren anh)
    /// quay khung quanh TAM. Ca bon dinh phai nam trong anh. IoU co khung xoay = IoU da giac cua bon dinh
    /// (HinhHoc.IouDaGiac, luoi 100 x 100 — cung cach voi polygon); hai khung khong xoay van tinh chinh xac.
    /// Gop: trung vi x, y, w, h va trung vi angle.
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
            double goc = Goc(p);

            if (p["angle"] != null && !tool.Rotatable)
            {
                throw Sai(tool, "công cụ này không cho xoay khung.");
            }

            if (Math.Abs(goc) > HinhHoc.SaiSo)
            {
                foreach (double[] d in Dinh(x, y, w, h, goc))
                {
                    if (d[0] < -HinhHoc.SaiSo || d[1] < -HinhHoc.SaiSo
                        || (mau.Width.HasValue && d[0] > mau.Width.Value + HinhHoc.SaiSo)
                        || (mau.Height.HasValue && d[1] > mau.Height.Value + HinhHoc.SaiSo))
                    {
                        throw Sai(tool, "khung xoay có đỉnh nằm ngoài ảnh.");
                    }
                }

                return;
            }

            if (mau.Width.HasValue && x + w > mau.Width.Value + HinhHoc.SaiSo)
            {
                throw Sai(tool, "khung vượt quá chiều rộng ảnh (" + mau.Width.Value + "px).");
            }

            if (mau.Height.HasValue && y + h > mau.Height.Value + HinhHoc.SaiSo)
            {
                throw Sai(tool, "khung vượt quá chiều cao ảnh (" + mau.Height.Value + "px).");
            }
        }

        protected override object PhanTich(JsonObject phanTu)
        {
            return new double[] { HinhHoc.So(phanTu["x"]), HinhHoc.So(phanTu["y"]), HinhHoc.So(phanTu["w"]), HinhHoc.So(phanTu["h"]), Goc(phanTu) };
        }

        protected override double DoGiongDaPhanTich(object a, object b, double tyLe)
        {
            double[] k1 = (double[])a;
            double[] k2 = (double[])b;
            if (Math.Abs(k1[4]) <= HinhHoc.SaiSo && Math.Abs(k2[4]) <= HinhHoc.SaiSo)
            {
                return HinhHoc.IouHop(k1[0], k1[1], k1[2], k1[3], k2[0], k2[1], k2[2], k2[3]);
            }

            List<double[]> d1 = Dinh(k1[0], k1[1], k1[2], k1[3], k1[4]);
            List<double[]> d2 = Dinh(k2[0], k2[1], k2[2], k2[3], k2[4]);
            if (!HinhHoc.HinhBaoGiaoNhau(d1, d2))
            {
                return 0;
            }

            return HinhHoc.IouDaGiac(d1, d2);
        }

        /// <summary>Goc xoay (do), khong co = 0.</summary>
        internal static double Goc(JsonObject p)
        {
            return p["angle"] == null ? 0 : HinhHoc.So(p["angle"]);
        }

        /// <summary>Bon dinh cua khung (x, y, w, h) xoay goc (do) quanh tam, theo thu tu tren-trai → tren-phai → duoi-phai → duoi-trai.</summary>
        internal static List<double[]> Dinh(double x, double y, double w, double h, double goc)
        {
            double cx = x + (w / 2);
            double cy = y + (h / 2);
            double rad = goc * Math.PI / 180;
            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);
            double[][] lech = new double[][]
            {
                new double[] { -w / 2, -h / 2 },
                new double[] { w / 2, -h / 2 },
                new double[] { w / 2, h / 2 },
                new double[] { -w / 2, h / 2 },
            };

            List<double[]> ds = new List<double[]>();
            foreach (double[] l in lech)
            {
                ds.Add(new double[] { cx + (l[0] * cos) - (l[1] * sin), cy + (l[0] * sin) + (l[1] * cos) });
            }

            return ds;
        }

        protected override double DoGiong(JsonObject a, JsonObject b, double tyLe)
        {
            return DoGiongDaPhanTich(PhanTich(a), PhanTich(b), tyLe);
        }

        protected override JsonObject DaiDien(IReadOnlyList<JsonObject> thanhVien, BoSoSanh so)
        {
            JsonObject dd = new JsonObject
            {
                ["x"] = HinhHoc.LamTron(HinhHoc.TrungVi(thanhVien.Select(m => HinhHoc.So(m["x"])))),
                ["y"] = HinhHoc.LamTron(HinhHoc.TrungVi(thanhVien.Select(m => HinhHoc.So(m["y"])))),
                ["w"] = HinhHoc.LamTron(HinhHoc.TrungVi(thanhVien.Select(m => HinhHoc.So(m["w"])))),
                ["h"] = HinhHoc.LamTron(HinhHoc.TrungVi(thanhVien.Select(m => HinhHoc.So(m["h"])))),
            };

            if (thanhVien.Any(m => m["angle"] != null))
            {
                dd["angle"] = HinhHoc.LamTron(HinhHoc.TrungVi(thanhVien.Select(m => Goc(m))));
            }

            return dd;
        }

        protected override string KhoaSapXep(JsonObject p)
        {
            return p["labelId"]!.GetValue<string>() + "|" + SoDeSapXep(HinhHoc.So(p["x"])) + "|" + SoDeSapXep(HinhHoc.So(p["y"]))
                   + "|" + SoDeSapXep(HinhHoc.So(p["w"])) + "|" + SoDeSapXep(HinhHoc.So(p["h"]));
        }
    }
}
