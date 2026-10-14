using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Crowd.Labeling.Tools
{
    /// <summary>
    /// Mat na phan vung (to bang co) tren anh, ma hoa RLE khong nen kieu COCO (xem Rle):
    ///     [{"labelId":"troi","rle":{"size":[cao, rong],"counts":[so pixel 0, so pixel 1, ...]}}]
    ///
    /// Luat: size trung kich thuoc anh; tong counts = cao x rong; chi doan DAU duoc dai 0 (dang
    /// chuan — hai mat na giong nhau ra cung mot chuoi); mat na phai co it nhat mot pixel.
    /// Cac mat na duoc phep chong nhau (hai vat che nhau).
    ///
    /// Cham dap an: IoU tung cap &gt;= nguong (mac dinh 0.5).
    /// Gop nhieu nguoi: ghep cum theo IoU, mat na dai dien = bo phieu TUNG PIXEL trong cum (pixel
    /// duoc bat khi hon mot nua so mat na cua cum bat no).
    /// </summary>
    internal sealed class MaskTool : DanhSachToolBase
    {
        /// <summary>Tong so doan cua moi mat na trong mot nhan — chan nhan vai chuc MB.</summary>
        public const int TongDoanToiDa = 200000;

        public override string Kind
        {
            get { return ToolKinds.Mask; }
        }

        protected override double NguongMacDinh
        {
            get { return 0.5; }
        }

        protected override void KiemPhanTu(JsonObject p, ToolDefinition tool, SampleMetadata mau)
        {
            RleMask m = Rle.Doc(p);

            if (mau.Height.HasValue && mau.Width.HasValue && (m.Cao != mau.Height.Value || m.Rong != mau.Width.Value))
            {
                throw Sai(tool, "mat na " + m.Rong + "x" + m.Cao + " khac kich thuoc anh " + mau.Width.Value + "x" + mau.Height.Value + ".");
            }

            for (int i = 1; i < m.Doan.Length; i++)
            {
                if (m.Doan[i] == 0)
                {
                    throw Sai(tool, "counts khong o dang chuan: chi doan dau tien duoc dai 0.");
                }
            }

            if (Rle.TongPixel(m.Doan) != (long)m.Cao * m.Rong)
            {
                throw Sai(tool, "tong counts phai bang so pixel cua anh (" + ((long)m.Cao * m.Rong) + ").");
            }

            // Ghi lai dang so nguyen (8.0 va 8 la mot do dai) — ban chuan phai ra cung mot chuoi.
            p["rle"] = Rle.ToJson(m.Cao, m.Rong, m.Doan);
        }

        protected override void KiemCaDanhSach(List<JsonObject> ds, ToolDefinition tool)
        {
            long tong = 0;
            foreach (JsonObject p in ds)
            {
                tong = tong + ((JsonArray)p["rle"]!["counts"]!).Count;
            }

            if (tong > TongDoanToiDa)
            {
                throw Sai(tool, "mat na qua chi tiet: tong so doan " + tong + " vuot " + TongDoanToiDa + ".");
            }
        }

        protected override object PhanTich(JsonObject phanTu)
        {
            return Rle.Doc(phanTu);
        }

        protected override double DoGiongDaPhanTich(object a, object b, double tyLe)
        {
            return Rle.Iou((RleMask)a, (RleMask)b);
        }

        protected override double DoGiong(JsonObject a, JsonObject b, double tyLe)
        {
            return Rle.Iou(Rle.Doc(a), Rle.Doc(b));
        }

        protected override JsonObject DaiDien(IReadOnlyList<JsonObject> thanhVien, BoSoSanh so)
        {
            RleMask dau = Rle.Doc(thanhVien[0]);
            List<long[]> ds = new List<long[]>();
            foreach (JsonObject m in thanhVien)
            {
                ds.Add(Rle.Doc(m).Doan);
            }

            List<long> daSo = Rle.DaSo(ds);
            if (daSo.Count < 2)
            {
                // Khong pixel nao qua ban (chi xay ra khi nguong ghep rat thap) — lay mat na "o giua".
                return base.DaiDien(thanhVien, so);
            }

            return new JsonObject { ["rle"] = Rle.ToJson(dau.Cao, dau.Rong, daSo) };
        }

        protected override string KhoaSapXep(JsonObject p)
        {
            JsonArray doan = (JsonArray)p["rle"]!["counts"]!;
            return p["labelId"]!.GetValue<string>() + "|" + SoDeSapXep(HinhHoc.SoNguyenDai(doan[0], "nhan_khong_hop_le")) + "|" + SoDeSapXep(doan.Count);
        }
    }
}
