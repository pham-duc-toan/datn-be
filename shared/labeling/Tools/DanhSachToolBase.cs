using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Crowd.Labeling.Tools
{
    /// <summary>
    /// Khung chung cho cong cu ket qua la DANH SACH doi tuong co lop (bbox, polygon,
    /// keypoint, polyline, mask, span, relation, temporalSegment, track):
    ///   - so phan tu trong [minItems, maxItems];
    ///   - labelId cua tung phan tu co trong tap lop cua cong cu;
    ///   - kiem rieng tung phan tu (lop con lam) va thuoc tinh cua no;
    ///   - CHUAN HOA: sap xep cac phan tu — danh sach khung la tap hop, thu tu ve
    ///     khong co nghia, nen hai nhan cung cac khung phai ra cung chuoi JSON.
    ///
    /// So khop dap an (mac dinh): so phan tu bang nhau, va ghep tham lam moi phan
    /// tu cua dap an voi phan tu chua ghep CUNG LOP giong nhat; moi cap phai dat
    /// do giong &gt;= nguong va khop thuoc tinh. Span / quan he: F1 giua hai danh sach (DiemF1).
    /// </summary>
    internal abstract class DanhSachToolBase : IToolKind
    {
        public abstract string Kind { get; }

        /// <summary>Nguong do giong mac dinh khi cong cu khong khai matchThreshold.</summary>
        protected abstract double NguongMacDinh { get; }

        /// <summary>Kiem mot phan tu theo thong tin mau. Sai thi nem qua Sai(...).</summary>
        protected abstract void KiemPhanTu(JsonObject phanTu, ToolDefinition tool, SampleMetadata mau);

        /// <summary>
        /// Do giong 0..1 cua hai phan tu CUNG LOP (IoU...). tyLe: thuoc do de chuan hoa do lech
        /// (duong cheo anh) — chi cong cu do bang khoang cach (keypoint, polyline) dung toi.
        /// </summary>
        protected abstract double DoGiong(JsonObject a, JsonObject b, double tyLe);

        /// <summary>Khoa sap xep de chuan hoa.</summary>
        protected abstract string KhoaSapXep(JsonObject phanTu);

        /// <summary>
        /// Ban DA DOC cua mot doi tuong (vd mat na thanh mang doan, da giac thanh mang dinh) — moi doi tuong
        /// chi doc JSON mot lan trong mot lan cham (xem BoSoSanh). Mac dinh: chinh doi tuong JSON.
        /// </summary>
        protected virtual object PhanTich(JsonObject phanTu)
        {
            return phanTu;
        }

        /// <summary>Do giong tren hai ban da doc (PhanTich). Mac dinh goi DoGiong tren JSON.</summary>
        protected virtual double DoGiongDaPhanTich(object a, object b, double tyLe)
        {
            return DoGiong((JsonObject)a, (JsonObject)b, tyLe);
        }

        /// <summary>Thuoc do chuan hoa do lech cho cac phan tu dang xet. Mac dinh khong dung (1).</summary>
        protected virtual double TyLe(SampleMetadata mau, IEnumerable<JsonObject> tatCa)
        {
            return 1;
        }

        /// <summary>Nguong de coi hai phan tu la MOT khi ghep cap cham dap an.</summary>
        protected virtual double NguongGhep(ToolDefinition tool, double? nguongMacDinh)
        {
            return tool.MatchThreshold ?? nguongMacDinh ?? NguongMacDinh;
        }

        public JsonNode KiemVaChuanHoa(JsonNode ketQua, ToolDefinition tool, SampleMetadata mau)
        {
            List<JsonObject> ds = DocDanhSach(ketQua);

            if (ds.Count < tool.MinItems || ds.Count > tool.MaxItems)
            {
                throw Sai(tool, "số đối tượng phải từ " + tool.MinItems + " đến " + tool.MaxItems + " (đang có " + ds.Count + ").");
            }

            List<JsonObject> chuanHoa = new List<JsonObject>();
            foreach (JsonObject goc in ds)
            {
                JsonObject p = (JsonObject)goc.DeepClone();
                string lop = p["labelId"]!.GetValue<string>();
                if (!tool.CoLop(lop))
                {
                    throw Sai(tool, "lớp “" + lop + "” không có trong tập nhãn.");
                }

                KiemPhanTu(p, tool, mau);
                ThuocTinh.KiemVaChuanHoa(p, tool);
                chuanHoa.Add(p);
            }

            KiemCaDanhSach(chuanHoa, tool);

            JsonArray chuan = new JsonArray();
            foreach (JsonObject p in chuanHoa.OrderBy(KhoaSapXep, StringComparer.Ordinal))
            {
                chuan.Add(p);
            }

            return chuan;
        }

        /// <summary>Luat tren CA danh sach (vd span khong duoc chong nhau). Mac dinh khong co.</summary>
        protected virtual void KiemCaDanhSach(List<JsonObject> ds, ToolDefinition tool)
        {
        }

        public virtual bool Khop(JsonNode nop, JsonNode dapAn, ToolDefinition tool, double? nguongMacDinh, SampleMetadata mau)
        {
            List<JsonObject> a = DocDanhSach(nop);
            List<JsonObject> b = DocDanhSach(dapAn);

            if (a.Count != b.Count)
            {
                return false;
            }

            double nguong = NguongGhep(tool, nguongMacDinh);
            BoSoSanh so = new BoSoSanh(this, TyLe(mau, a.Concat(b)));
            bool[] daGhep = new bool[a.Count];

            foreach (JsonObject dung in b)
            {
                string lop = dung["labelId"]!.GetValue<string>();
                int tot = -1;
                double giongNhat = -1;

                for (int i = 0; i < a.Count; i++)
                {
                    if (daGhep[i] || a[i]["labelId"]!.GetValue<string>() != lop)
                    {
                        continue;
                    }

                    double g = so.Giong(a[i], dung);
                    if (g > giongNhat)
                    {
                        giongNhat = g;
                        tot = i;
                    }
                }

                if (tot < 0 || giongNhat < nguong || !ThuocTinh.Khop(dung, a[tot], tool))
                {
                    return false;
                }

                daGhep[tot] = true;
            }

            return true;
        }

        /// <summary>
        /// F1 giua cac doi tuong cua mot nguoi va ban chuan: ghep mot-mot (uu tien cap giong nhat),
        /// mot cap tinh la dung khi cung lop, do giong &gt;= nguong va khop thuoc tinh cua ban chuan.
        /// Ca hai rong = 1.
        /// </summary>
        protected static double DiemF1(List<JsonObject> cuaNguoi, List<JsonObject> chuan, ToolDefinition tool, double nguong, BoSoSanh so)
        {
            if (cuaNguoi.Count == 0 && chuan.Count == 0)
            {
                return 1;
            }

            List<CapGhep> cap = new List<CapGhep>();
            for (int o = 0; o < cuaNguoi.Count; o++)
            {
                string lop = cuaNguoi[o]["labelId"]!.GetValue<string>();
                for (int c = 0; c < chuan.Count; c++)
                {
                    if (chuan[c]["labelId"]!.GetValue<string>() != lop)
                    {
                        continue;
                    }

                    double g = so.Giong(cuaNguoi[o], chuan[c]);
                    if (g >= nguong - HinhHoc.SaiSoNguong && ThuocTinh.Khop(chuan[c], cuaNguoi[o], tool))
                    {
                        cap.Add(new CapGhep(g, c, o));
                    }
                }
            }

            cap.Sort(CapGhep.SoSanh);
            bool[] daDung = new bool[cuaNguoi.Count];
            bool[] chuanDaGhep = new bool[chuan.Count];
            int dung = 0;

            foreach (CapGhep g in cap)
            {
                if (daDung[g.PhanTu] || chuanDaGhep[g.Dich])
                {
                    continue;
                }

                daDung[g.PhanTu] = true;
                chuanDaGhep[g.Dich] = true;
                dung++;
            }

            return (2.0 * dung) / (cuaNguoi.Count + chuan.Count);
        }

        protected static List<JsonObject> DocDanhSach(JsonNode ketQua)
        {
            List<JsonObject> ds = new List<JsonObject>();
            foreach (JsonNode? n in (JsonArray)ketQua)
            {
                ds.Add((JsonObject)n!);
            }

            return ds;
        }

        protected static LabelFormatException Sai(ToolDefinition tool, string loi)
        {
            return new LabelFormatException("nhan_khong_hop_le", TenHienThi.TienToNhan(tool) + loi);
        }

        /// <summary>So dang chuoi co dinh do dai — de sap xep theo so ma van so sanh chuoi.</summary>
        protected static string SoDeSapXep(double so)
        {
            return so.ToString("0000000000.000", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// So do giong trong MOT lan cham: moi doi tuong chi doc (PhanTich) mot lan, moi cap chi tinh mot
        /// lan (do giong doi xung nen (a, b) va (b, a) dung chung). Ket qua y het goi DoGiong truc tiep.
        /// </summary>
        protected sealed class BoSoSanh
        {
            private readonly DanhSachToolBase _congCu;
            private readonly double _tyLe;
            private readonly Dictionary<JsonObject, object> _daDoc = new Dictionary<JsonObject, object>(ReferenceEqualityComparer.Instance);
            private readonly Dictionary<JsonObject, Dictionary<JsonObject, double>> _daSo =
                new Dictionary<JsonObject, Dictionary<JsonObject, double>>(ReferenceEqualityComparer.Instance);

            public BoSoSanh(DanhSachToolBase congCu, double tyLe)
            {
                _congCu = congCu;
                _tyLe = tyLe;
            }

            public double Giong(JsonObject a, JsonObject b)
            {
                Dictionary<JsonObject, double>? cuaA;
                double g;
                if (_daSo.TryGetValue(a, out cuaA) && cuaA.TryGetValue(b, out g))
                {
                    return g;
                }

                g = _congCu.DoGiongDaPhanTich(Doc(a), Doc(b), _tyLe);
                Ghi(a, b, g);
                Ghi(b, a, g);
                return g;
            }

            private object Doc(JsonObject p)
            {
                object? ban;
                if (!_daDoc.TryGetValue(p, out ban))
                {
                    ban = _congCu.PhanTich(p);
                    _daDoc[p] = ban;
                }

                return ban;
            }

            private void Ghi(JsonObject a, JsonObject b, double g)
            {
                Dictionary<JsonObject, double>? cuaA;
                if (!_daSo.TryGetValue(a, out cuaA))
                {
                    cuaA = new Dictionary<JsonObject, double>(ReferenceEqualityComparer.Instance);
                    _daSo[a] = cuaA;
                }

                cuaA[b] = g;
            }
        }

        /// <summary>Mot cap ung vien (doi tuong cua nguoi dang xet, doi tuong chuan) va do giong.</summary>
        private sealed class CapGhep
        {
            public CapGhep(double doGiong, int dich, int phanTu)
            {
                DoGiong = doGiong;
                Dich = dich;
                PhanTu = phanTu;
            }

            public double DoGiong { get; }

            public int Dich { get; }

            public int PhanTu { get; }

            /// <summary>Giong nhat truoc; hoa thi dich nho truoc, roi phan tu nho truoc — ket qua tat dinh.</summary>
            public static int SoSanh(CapGhep a, CapGhep b)
            {
                int c = b.DoGiong.CompareTo(a.DoGiong);
                if (c != 0)
                {
                    return c;
                }

                c = a.Dich.CompareTo(b.Dich);
                if (c != 0)
                {
                    return c;
                }

                return a.PhanTu.CompareTo(b.PhanTu);
            }
        }
    }
}
