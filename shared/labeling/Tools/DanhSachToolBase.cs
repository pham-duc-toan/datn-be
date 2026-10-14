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
    /// do giong &gt;= nguong va khop thuoc tinh.
    ///
    /// GOP nhan cua nhieu nguoi (Gop): ghep cac doi tuong "cung mot vat" cua nhung nguoi khac
    /// nhau thanh CUM, giu cum duoc hon mot nua so nguoi ve, moi cum ra mot doi tuong dai dien.
    /// </summary>
    internal abstract class DanhSachToolBase : IToolKind
    {
        public abstract string Kind { get; }

        /// <summary>Nguong do giong mac dinh khi cong cu khong khai matchThreshold.</summary>
        protected abstract double NguongMacDinh { get; }

        /// <summary>Ten cach gop ghi vao ket qua ("cluster": ghep cum theo do giong; "majority": trung tung phan tu).</summary>
        protected virtual string CachGop
        {
            get { return "cluster"; }
        }

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
        /// Ban DA DOC cua mot doi tuong (vd mat na thanh mang doan, da giac thanh mang dinh). Khi gop, moi
        /// doi tuong bi so voi rat nhieu doi tuong khac; doc JSON lai moi lan so la phan ton thoi gian nhat
        /// (40 mat na x 5 nguoi: 7,8 giay → xem BoSoSanh). Mac dinh: chinh doi tuong JSON.
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

        /// <summary>Nguong de coi hai phan tu la MOT khi ghep cap (cham dap an, gop cum).</summary>
        protected virtual double NguongGhep(ToolDefinition tool, double? nguongMacDinh)
        {
            return tool.MatchThreshold ?? nguongMacDinh ?? NguongMacDinh;
        }

        /// <summary>
        /// Doi tuong DAI DIEN cho mot cum (chi phan hinh hoc, chua co labelId / attributes).
        /// Mac dinh: thanh vien "o giua" — tong do giong toi cac thanh vien khac lon nhat (hoa thi
        /// lay thanh vien dau). Cong cu co toa do cong duoc (khung, diem, doan) ghi de bang trung vi.
        /// </summary>
        protected virtual JsonObject DaiDien(IReadOnlyList<JsonObject> thanhVien, BoSoSanh so)
        {
            int tot = 0;
            double nhieuNhat = double.MinValue;

            for (int i = 0; i < thanhVien.Count; i++)
            {
                double tong = 0;
                for (int j = 0; j < thanhVien.Count; j++)
                {
                    if (i != j)
                    {
                        tong = tong + so.Giong(thanhVien[i], thanhVien[j]);
                    }
                }

                if (tong > nhieuNhat + HinhHoc.SaiSoNguong)
                {
                    nhieuNhat = tong;
                    tot = i;
                }
            }

            JsonObject ra = (JsonObject)thanhVien[tot].DeepClone();
            ra.Remove("labelId");
            ra.Remove(ThuocTinh.Khoa);
            return ra;
        }

        public JsonNode KiemVaChuanHoa(JsonNode ketQua, ToolDefinition tool, SampleMetadata mau)
        {
            List<JsonObject> ds = DocDanhSach(ketQua);

            if (ds.Count < tool.MinItems || ds.Count > tool.MaxItems)
            {
                throw Sai(tool, "so doi tuong phai tu " + tool.MinItems + " den " + tool.MaxItems + " (dang co " + ds.Count + ").");
            }

            List<JsonObject> chuanHoa = new List<JsonObject>();
            foreach (JsonObject goc in ds)
            {
                JsonObject p = (JsonObject)goc.DeepClone();
                string lop = p["labelId"]!.GetValue<string>();
                if (!tool.CoLop(lop))
                {
                    throw Sai(tool, "lop '" + lop + "' khong co trong tap nhan.");
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

        // =====================================================================
        // GOP NHAN CUA NHIEU NGUOI
        // =====================================================================

        /// <summary>
        /// 1. GHEP CUM: di lan luot tung nguoi. Voi moi nguoi, ghep cac doi tuong cua ho vao cac cum
        ///    DA CO (do nhung nguoi truoc tao) — cung lop, do giong voi doi tuong MO DAU cum &gt;= nguong,
        ///    uu tien cap giong nhat, moi cum nhan toi da mot doi tuong cua mot nguoi. Doi tuong khong
        ///    ghep duoc mo cum moi.
        /// 2. CHOT: giu cum co HON MOT NUA so nguoi; moi cum ra mot doi tuong dai dien + thuoc tinh gop.
        /// 3. DONG Y: mot nguoi dong y khi F1 giua doi tuong cua ho va ket qua chot &gt;= F1ToiThieu.
        ///    Khong qua mot nua so nguoi dong y → tranh chap, khong co ket qua (final = null).
        /// </summary>
        public JsonObject Gop(IReadOnlyList<JsonNode> ketQua, ToolDefinition tool, ThamSoGop thamSo)
        {
            List<List<JsonObject>> ds = new List<List<JsonObject>>();
            List<JsonObject> tatCa = new List<JsonObject>();
            foreach (JsonNode k in ketQua)
            {
                List<JsonObject> mot = DocDanhSach(k);
                ds.Add(mot);
                tatCa.AddRange(mot);
            }

            int soNguoi = ds.Count;
            double nguong = NguongGhep(tool, thamSo.NguongMacDinh);
            BoSoSanh so = new BoSoSanh(this, TyLe(thamSo.Mau, tatCa));

            // 1. Ghep cum.
            List<Cum> cum = new List<Cum>();
            foreach (List<JsonObject> cuaNguoi in ds)
            {
                int soCumCu = cum.Count;
                List<CapGhep> cap = new List<CapGhep>();

                for (int o = 0; o < cuaNguoi.Count; o++)
                {
                    string lop = cuaNguoi[o]["labelId"]!.GetValue<string>();
                    for (int c = 0; c < soCumCu; c++)
                    {
                        if (cum[c].Lop != lop)
                        {
                            continue;
                        }

                        double g = so.Giong(cuaNguoi[o], cum[c].ThanhVien[0]);
                        if (g >= nguong - HinhHoc.SaiSoNguong)
                        {
                            cap.Add(new CapGhep(g, c, o));
                        }
                    }
                }

                cap.Sort(CapGhep.SoSanh);
                bool[] daDung = new bool[cuaNguoi.Count];
                bool[] cumDaNhan = new bool[soCumCu];

                foreach (CapGhep g in cap)
                {
                    if (daDung[g.PhanTu] || cumDaNhan[g.Dich])
                    {
                        continue;
                    }

                    cum[g.Dich].ThanhVien.Add(cuaNguoi[g.PhanTu]);
                    daDung[g.PhanTu] = true;
                    cumDaNhan[g.Dich] = true;
                }

                for (int o = 0; o < cuaNguoi.Count; o++)
                {
                    if (!daDung[o])
                    {
                        cum.Add(new Cum(cuaNguoi[o]["labelId"]!.GetValue<string>(), cuaNguoi[o]));
                    }
                }
            }

            // 2. Chot cum duoc da so ung ho.
            List<KeyValuePair<JsonObject, int>> chot = new List<KeyValuePair<JsonObject, int>>();
            foreach (Cum c in cum)
            {
                if (c.ThanhVien.Count * 2 <= soNguoi)
                {
                    continue;
                }

                JsonObject dd = DaiDien(c.ThanhVien, so);
                dd["labelId"] = c.Lop;

                JsonObject? thuocTinh = ThuocTinh.Gop(c.ThanhVien, tool);
                if (thuocTinh != null)
                {
                    dd[ThuocTinh.Khoa] = thuocTinh;
                }

                chot.Add(new KeyValuePair<JsonObject, int>(dd, c.ThanhVien.Count));
            }

            chot = chot.OrderBy(k => KhoaSapXep(k.Key), StringComparer.Ordinal).ToList();
            List<JsonObject> doiTuongChot = chot.Select(k => k.Key).ToList();

            // 3. Tung nguoi co dong y voi ket qua chot khong. MOT nguoi thi ket qua chot chinh la nhan cua ho
            //    (toa do lam tron 3 chu so) — luon khop, khoi so tung cap (O(n²) tren mau nhieu doi tuong).
            JsonArray dongY = new JsonArray();
            int soDongY = 0;
            foreach (List<JsonObject> cuaNguoi in ds)
            {
                bool khop = soNguoi == 1
                    || DiemF1(cuaNguoi, doiTuongChot, tool, nguong, so) >= thamSo.F1ToiThieu - HinhHoc.SaiSoNguong;
                dongY.Add(khop);
                if (khop)
                {
                    soDongY++;
                }
            }

            bool tranhChap = soDongY * 2 <= soNguoi;

            JsonObject ra = new JsonObject();
            ra["method"] = CachGop;
            if (tranhChap)
            {
                ra["final"] = null;
            }
            else
            {
                JsonArray cuoi = new JsonArray();
                JsonArray ungHo = new JsonArray();
                foreach (KeyValuePair<JsonObject, int> k in chot)
                {
                    cuoi.Add(k.Key);
                    ungHo.Add(k.Value);
                }

                ra["final"] = cuoi;
                ra["supports"] = ungHo;
            }

            ra["agrees"] = dongY;
            ra["disputed"] = tranhChap;
            return ra;
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
            return new LabelFormatException("nhan_khong_hop_le", "Cong cu '" + tool.Name + "': " + loi);
        }

        /// <summary>So dang chuoi co dinh do dai — de sap xep theo so ma van so sanh chuoi.</summary>
        protected static string SoDeSapXep(double so)
        {
            return so.ToString("0000000000.000", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// So do giong trong MOT lan cham / gop: moi doi tuong chi doc (PhanTich) mot lan, moi cap chi tinh
        /// mot lan (cung cap bi hoi lai o buoc ghep cum, chon thanh vien "o giua" va cham F1; do giong doi
        /// xung nen (a, b) va (b, a) dung chung). Ket qua y het goi DoGiong truc tiep — chi bot viec lap lai.
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

        /// <summary>Mot cum doi tuong "cung mot vat": phan tu dau la doi tuong MO DAU cum.</summary>
        private sealed class Cum
        {
            public Cum(string lop, JsonObject moDau)
            {
                Lop = lop;
                ThanhVien = new List<JsonObject>();
                ThanhVien.Add(moDau);
            }

            public string Lop { get; }

            public List<JsonObject> ThanhVien { get; }
        }

        /// <summary>Mot cap ung vien (doi tuong cua nguoi dang xet, cum / doi tuong chuan) va do giong.</summary>
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
