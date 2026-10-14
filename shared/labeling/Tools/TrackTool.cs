using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Crowd.Labeling.Tools
{
    /// <summary>
    /// Theo doi doi tuong trong video: moi doi tuong la mot CHUOI KHUNG o cac moc thoi gian
    ///     [{"labelId":"xe","keyframes":[{"t":0,"x":10,"y":20,"w":50,"h":30},{"t":2.5,"x":80,"y":20,"w":50,"h":30}]}]
    /// t tinh bang GIAY tu dau mau, tang dan, it nhat hai moc. Giua hai moc khung duoc NOI SUY tuyen
    /// tinh; doi tuong ton tai tu moc dau toi moc cuoi (bien mat roi xuat hien lai = mot chuoi moi).
    /// Moi khung nam trong khung hinh video, moi moc nam trong do dai mau.
    ///
    /// Do giong hai chuoi = IoU KHONG-THOI GIAN: lay mau deu SoMocMauTrack thoi diem tren khoang hop
    /// cua hai chuoi, tong dien tich giao / tong dien tich hop (chuoi chua / khong con ton tai o thoi
    /// diem do coi nhu dien tich 0). Vua phat lech vi tri, vua phat lech thoi diem bat dau / ket thuc.
    /// Cham dap an: &gt;= nguong (mac dinh 0.5). Gop nhieu nguoi: ghep cum, lay chuoi "o giua".
    /// </summary>
    internal sealed class TrackTool : DanhSachToolBase
    {
        public override string Kind
        {
            get { return ToolKinds.Track; }
        }

        protected override double NguongMacDinh
        {
            get { return 0.5; }
        }

        protected override void KiemPhanTu(JsonObject p, ToolDefinition tool, SampleMetadata mau)
        {
            List<double[]> moc = Moc(p);
            if (moc.Count < 2)
            {
                throw Sai(tool, "chuoi khung can it nhat hai moc thoi gian.");
            }

            for (int i = 0; i < moc.Count; i++)
            {
                double[] k = moc[i];

                if (i > 0 && k[0] <= moc[i - 1][0])
                {
                    throw Sai(tool, "moc thoi gian phai tang dan (moc " + k[0] + "s dung sau " + moc[i - 1][0] + "s).");
                }

                if (mau.DurationSec.HasValue && k[0] > mau.DurationSec.Value + HinhHoc.SaiSo)
                {
                    throw Sai(tool, "moc " + k[0] + "s vuot qua do dai mau " + mau.DurationSec.Value + "s.");
                }

                if (mau.Width.HasValue && k[1] + k[3] > mau.Width.Value + HinhHoc.SaiSo)
                {
                    throw Sai(tool, "khung o moc " + k[0] + "s vuot qua chieu rong video (" + mau.Width.Value + "px).");
                }

                if (mau.Height.HasValue && k[2] + k[4] > mau.Height.Value + HinhHoc.SaiSo)
                {
                    throw Sai(tool, "khung o moc " + k[0] + "s vuot qua chieu cao video (" + mau.Height.Value + "px).");
                }
            }
        }

        protected override object PhanTich(JsonObject phanTu)
        {
            return Moc(phanTu);
        }

        protected override double DoGiong(JsonObject a, JsonObject b, double tyLe)
        {
            return DoGiongDaPhanTich(Moc(a), Moc(b), tyLe);
        }

        protected override double DoGiongDaPhanTich(object a, object b, double tyLe)
        {
            List<double[]> ma = (List<double[]>)a;
            List<double[]> mb = (List<double[]>)b;

            double dau = Math.Min(ma[0][0], mb[0][0]);
            double cuoi = Math.Max(ma[ma.Count - 1][0], mb[mb.Count - 1][0]);
            if (cuoi <= dau)
            {
                return 0;
            }

            double tongGiao = 0;
            double tongHop = 0;
            for (int k = 0; k < HinhHoc.SoMocMauTrack; k++)
            {
                double t = dau + ((k + 0.5) * (cuoi - dau) / HinhHoc.SoMocMauTrack);
                double[]? ka = KhungTai(ma, t);
                double[]? kb = KhungTai(mb, t);

                double dienTichA = ka == null ? 0 : ka[2] * ka[3];
                double dienTichB = kb == null ? 0 : kb[2] * kb[3];
                double giao = 0;

                if (ka != null && kb != null)
                {
                    double rong = Math.Min(ka[0] + ka[2], kb[0] + kb[2]) - Math.Max(ka[0], kb[0]);
                    double cao = Math.Min(ka[1] + ka[3], kb[1] + kb[3]) - Math.Max(ka[1], kb[1]);
                    giao = Math.Max(0, rong) * Math.Max(0, cao);
                }

                tongGiao = tongGiao + giao;
                tongHop = tongHop + dienTichA + dienTichB - giao;
            }

            return tongHop <= 0 ? 0 : tongGiao / tongHop;
        }

        protected override string KhoaSapXep(JsonObject p)
        {
            List<double[]> moc = Moc(p);
            return p["labelId"]!.GetValue<string>() + "|" + SoDeSapXep(moc[0][0]) + "|" + SoDeSapXep(moc[0][1]) + "|" + SoDeSapXep(moc[0][2]);
        }

        /// <summary>Cac moc cua chuoi: moi moc la [t, x, y, w, h].</summary>
        internal static List<double[]> Moc(JsonObject p)
        {
            List<double[]> ds = new List<double[]>();
            foreach (JsonNode? n in (JsonArray)p["keyframes"]!)
            {
                ds.Add(new double[]
                {
                    HinhHoc.So(n!["t"]), HinhHoc.So(n["x"]), HinhHoc.So(n["y"]), HinhHoc.So(n["w"]), HinhHoc.So(n["h"]),
                });
            }

            return ds;
        }

        /// <summary>Khung [x, y, w, h] cua chuoi tai thoi diem t (noi suy tuyen tinh); null neu doi tuong khong ton tai luc do.</summary>
        internal static double[]? KhungTai(List<double[]> moc, double t)
        {
            if (t < moc[0][0] || t > moc[moc.Count - 1][0])
            {
                return null;
            }

            for (int i = 1; i < moc.Count; i++)
            {
                if (t <= moc[i][0])
                {
                    double[] truoc = moc[i - 1];
                    double[] sau = moc[i];
                    double f = (t - truoc[0]) / (sau[0] - truoc[0]);
                    return new double[]
                    {
                        truoc[1] + (f * (sau[1] - truoc[1])),
                        truoc[2] + (f * (sau[2] - truoc[2])),
                        truoc[3] + (f * (sau[3] - truoc[3])),
                        truoc[4] + (f * (sau[4] - truoc[4])),
                    };
                }
            }

            double[] chot = moc[moc.Count - 1];
            return new double[] { chot[1], chot[2], chot[3], chot[4] };
        }
    }
}
