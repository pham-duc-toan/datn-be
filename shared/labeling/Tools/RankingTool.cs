using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Crowd.Labeling.Tools
{
    /// <summary>
    /// Xep hang cac muc cua mau danh sach (du lieu "list"): {"order":[2,0,1]} — chi so muc (tu 0)
    /// theo thu tu TOT NHAT → KEM NHAT, moi muc dung mot lan, du moi muc cua mau.
    ///
    /// Do giong hai thu tu = ti le cap muc duoc xep cung chieu (Kendall). Cham dap an: &gt;= nguong
    /// (mac dinh 0.8 — voi 3 muc la phai dung het, voi 5 muc cho lech toi da 2 trong 10 cap).
    /// Gop nhieu nguoi: DIEM BORDA — muc dung thu k (tu 0) trong m muc duoc m−1−k diem, cong diem
    /// cua moi nguoi, xep giam dan (hoa thi muc co chi so nho dung truoc).
    /// </summary>
    internal sealed class RankingTool : IToolKind
    {
        public const double NguongMacDinh = 0.8;

        public string Kind
        {
            get { return ToolKinds.Ranking; }
        }

        public JsonNode KiemVaChuanHoa(JsonNode ketQua, ToolDefinition tool, SampleMetadata mau)
        {
            List<int> thuTu = DocThuTu(ketQua);

            if (mau.ItemCount.HasValue)
            {
                if (thuTu.Count != mau.ItemCount.Value)
                {
                    throw Sai(tool, "phải xếp đủ " + mau.ItemCount.Value + " mục (đang có " + thuTu.Count + ").");
                }

                foreach (int muc in thuTu)
                {
                    if (muc >= mau.ItemCount.Value)
                    {
                        throw Sai(tool, "mục " + muc + " không tồn tại (mẫu có " + mau.ItemCount.Value + " mục, đánh số từ 0).");
                    }
                }
            }

            return TaoKetQua(thuTu);
        }

        public bool Khop(JsonNode nop, JsonNode dapAn, ToolDefinition tool, double? nguongMacDinh, SampleMetadata mau)
        {
            double nguong = tool.MatchThreshold ?? nguongMacDinh ?? NguongMacDinh;
            return HinhHoc.Kendall(DocThuTu(nop), DocThuTu(dapAn)) >= nguong - HinhHoc.SaiSoNguong;
        }

        public JsonObject Gop(IReadOnlyList<JsonNode> ketQua, ToolDefinition tool, ThamSoGop thamSo)
        {
            List<List<int>> ds = new List<List<int>>();
            SortedDictionary<int, int> diem = new SortedDictionary<int, int>();

            foreach (JsonNode k in ketQua)
            {
                List<int> thuTu = DocThuTu(k);
                ds.Add(thuTu);
                for (int viTri = 0; viTri < thuTu.Count; viTri++)
                {
                    int cu;
                    diem.TryGetValue(thuTu[viTri], out cu);
                    diem[thuTu[viTri]] = cu + (thuTu.Count - 1 - viTri);
                }
            }

            List<int> chot = diem.OrderByDescending(d => d.Value).ThenBy(d => d.Key).Select(d => d.Key).ToList();

            double nguong = tool.MatchThreshold ?? thamSo.NguongMacDinh ?? NguongMacDinh;
            JsonArray dongY = new JsonArray();
            int soDongY = 0;
            foreach (List<int> thuTu in ds)
            {
                bool khop = HinhHoc.Kendall(thuTu, chot) >= nguong - HinhHoc.SaiSoNguong;
                dongY.Add(khop);
                if (khop)
                {
                    soDongY++;
                }
            }

            JsonObject diemTheoMuc = new JsonObject();
            foreach (KeyValuePair<int, int> d in diem)
            {
                diemTheoMuc[d.Key.ToString(System.Globalization.CultureInfo.InvariantCulture)] = d.Value;
            }

            bool tranhChap = soDongY * 2 <= ds.Count;
            return new JsonObject
            {
                ["method"] = "borda",
                ["final"] = tranhChap ? null : TaoKetQua(chot),
                ["scores"] = diemTheoMuc,
                ["agrees"] = dongY,
                ["disputed"] = tranhChap,
            };
        }

        internal static List<int> DocThuTu(JsonNode ketQua)
        {
            List<int> ds = new List<int>();
            foreach (JsonNode? n in (JsonArray)ketQua["order"]!)
            {
                ds.Add(HinhHoc.SoNguyen(n));
            }

            return ds;
        }

        private static JsonObject TaoKetQua(IEnumerable<int> thuTu)
        {
            JsonArray mang = new JsonArray();
            foreach (int muc in thuTu)
            {
                mang.Add(muc);
            }

            return new JsonObject { ["order"] = mang };
        }

        private static LabelFormatException Sai(ToolDefinition tool, string loi)
        {
            return new LabelFormatException("nhan_khong_hop_le", TenHienThi.TienToNhan(tool) + loi);
        }
    }
}
