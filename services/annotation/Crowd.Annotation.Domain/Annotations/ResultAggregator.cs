using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Crowd.Labeling;

namespace Crowd.Annotation.Domain.Annotations
{
    /// <summary>Ket qua chot cho mot mau.</summary>
    public sealed class SampleResult
    {
        public SampleResult(
            Guid sampleId,
            int approvedCount,
            JsonObject tools,
            IReadOnlyList<LabelAnnotation> approved)
        {
            SampleId = sampleId;
            ApprovedCount = approvedCount;
            Tools = tools;
            Approved = approved;
        }

        public Guid SampleId { get; }

        public int ApprovedCount { get; }

        /// <summary>
        /// Ket qua gop THEO TUNG CONG CU (Crowd.Labeling.LabelAggregator), vd
        ///   {"loai":{"kind":"classification","method":"majority","final":{...},"votes":{...},"agrees":[...],"disputed":false},
        ///    "vat":{"kind":"bbox","method":"cluster","final":[{...}],"supports":[3],"agrees":[...],"disputed":false},
        ///    "mo_ta":{"kind":"freeText","method":"none"}}
        /// "none" = cong cu khong gop tu dong (cau tra loi mo) hoac chua ai lam — ket qua cuoi
        /// la cac nhan DA DUYET (Approved). final = null khi tranh chap.
        /// </summary>
        public JsonObject Tools { get; }

        /// <summary>Cac nhan da duyet cua mau, cu nhat truoc.</summary>
        public IReadOnlyList<LabelAnnotation> Approved { get; }

        /// <summary>Co cong cu nao gop ma khong ra ket qua duoc qua nua so nguoi ung ho (FB-22).</summary>
        public bool Disputed
        {
            get
            {
                foreach (KeyValuePair<string, JsonNode?> t in Tools)
                {
                    JsonValue? d = t.Value == null ? null : t.Value["disputed"] as JsonValue;
                    bool tranhChap;
                    if (d != null && d.TryGetValue(out tranhChap) && tranhChap)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }

    /// <summary>
    /// Chot ket qua cho moi mau tu cac nhan DA DUYET, theo TAP NHAN cua du an:
    /// moi cong cu gop theo cach cua loai cong cu do (Crowd.Labeling):
    ///
    ///   classification / pairwise → DA SO TUYET DOI (hon mot nua so nguoi):
    ///       3 nguoi: cho, cho, meo → cho;  2 nguoi: cho, meo → tranh chap (FB-22)
    ///   bbox / polygon / keypoint / polyline / mask / temporalSegment / track → GHEP CUM: cac doi
    ///       tuong "cung mot vat" cua nhung nguoi khac nhau (do giong &gt;= nguong) gom thanh cum, giu cum
    ///       duoc hon mot nua so nguoi ve, moi cum ra mot doi tuong dai dien;
    ///   span / relation → giu phan tu duoc hon mot nua so nguoi ghi y het;
    ///   transcription → ban "o giua";  number → trung vi;  ranking → diem Borda;
    ///   freeText → "none": cau tra loi mo, ket qua cuoi la cac nhan da duyet.
    ///
    /// quality-svc chay CUNG thuat toan tren moi nhan vua nop (ca nhan chua duyet) de tinh dong thuan
    /// va xin them nguoi; o day chi tinh tren nhan DA DUYET — day la ket qua giao cho doanh nghiep.
    /// </summary>
    public static class ResultAggregator
    {
        public static IReadOnlyList<SampleResult> Chot(LabelSchema tapNhan, IEnumerable<LabelAnnotation> daDuyet)
        {
            return Chot(tapNhan, daDuyet, NguongKhop.CuaThuVien, new Dictionary<Guid, SampleMetadata>(), LabelAggregator.F1ToiThieuMacDinh);
        }

        /// <param name="nguong">Nguong ghep doi tuong mac dinh theo loai cong cu (setting labeling.threshold.*).</param>
        /// <param name="thongTinMau">Thong tin tung mau (kich thuoc anh cho diem moc / duong gap khuc); thieu thi gop khong kem.</param>
        /// <param name="f1ToiThieu">Setting quality.consensus_min_f1.</param>
        public static IReadOnlyList<SampleResult> Chot(
            LabelSchema tapNhan,
            IEnumerable<LabelAnnotation> daDuyet,
            NguongKhop nguong,
            IReadOnlyDictionary<Guid, SampleMetadata> thongTinMau,
            double f1ToiThieu)
        {
            if (tapNhan == null)
            {
                throw new ArgumentNullException(nameof(tapNhan));
            }

            if (daDuyet == null)
            {
                throw new ArgumentNullException(nameof(daDuyet));
            }

            if (thongTinMau == null)
            {
                throw new ArgumentNullException(nameof(thongTinMau));
            }

            List<SampleResult> ketQua = new List<SampleResult>();

            foreach (IGrouping<Guid, LabelAnnotation> nhom in daDuyet.GroupBy(a => a.SampleId).OrderBy(g => g.Key))
            {
                List<LabelAnnotation> ds = nhom.OrderBy(a => a.SubmittedAt).ToList();
                List<LabelPayload> nhan = ds.Select(a => a.Payload).ToList();

                SampleMetadata? mau;
                thongTinMau.TryGetValue(nhom.Key, out mau);

                ketQua.Add(new SampleResult(nhom.Key, ds.Count, LabelAggregator.Gop(tapNhan, nhan, nguong, mau, f1ToiThieu), ds));
            }

            return ketQua;
        }

        /// <summary>
        /// Canh bao lech phan bo nhan (FB-24) tren ket qua PhanBo — ham thuan:
        ///   - lop_ap_dao: mot lop chiem tren nguongPhanTram % so nhan cua cong cu;
        ///   - lop_chua_co_nhan: lop khai trong tap nhan ma chua co nhan nao.
        /// Cong cu chua du toiThieu nhan thi chua canh bao (it du lieu, ty le chua co nghia).
        /// </summary>
        public static List<CanhBaoLech> CanhBaoLech(
            LabelSchema tapNhan, IReadOnlyDictionary<string, SortedDictionary<string, int>> phanBo, int nguongPhanTram, int toiThieu)
        {
            if (tapNhan == null)
            {
                throw new ArgumentNullException(nameof(tapNhan));
            }

            if (phanBo == null)
            {
                throw new ArgumentNullException(nameof(phanBo));
            }

            List<CanhBaoLech> ra = new List<CanhBaoLech>();
            foreach (ToolDefinition t in tapNhan.Tools)
            {
                SortedDictionary<string, int>? dem;
                if (t.Classes.Count < 2 || !phanBo.TryGetValue(t.Name, out dem))
                {
                    continue;
                }

                int tong = dem.Values.Sum();
                if (tong < toiThieu)
                {
                    continue;
                }

                foreach (string lop in t.Classes)
                {
                    int so;
                    if (!dem.TryGetValue(lop, out so))
                    {
                        so = 0;
                    }

                    int phanTram = (int)Math.Round(100.0 * so / tong);
                    if (so == 0)
                    {
                        ra.Add(new CanhBaoLech("lop_chua_co_nhan", t.Name, lop, so, tong, phanTram));
                    }
                    else if (phanTram > nguongPhanTram)
                    {
                        ra.Add(new CanhBaoLech("lop_ap_dao", t.Name, lop, so, tong, phanTram));
                    }
                }
            }

            return ra;
        }

        /// <summary>
        /// Phan bo nhan theo tung cong cu co lop — nen cua canh bao lech lop (FB-24):
        ///   gop theo da so (classification / pairwise): dem ket qua CHOT;
        ///   cong cu danh sach (bbox, span...): dem tung muc trong cac nhan da duyet.
        /// </summary>
        public static SortedDictionary<string, SortedDictionary<string, int>> PhanBo(
            LabelSchema tapNhan, IReadOnlyList<SampleResult> ketQua)
        {
            if (tapNhan == null)
            {
                throw new ArgumentNullException(nameof(tapNhan));
            }

            if (ketQua == null)
            {
                throw new ArgumentNullException(nameof(ketQua));
            }

            SortedDictionary<string, SortedDictionary<string, int>> ra =
                new SortedDictionary<string, SortedDictionary<string, int>>(StringComparer.Ordinal);

            foreach (ToolDefinition t in tapNhan.Tools)
            {
                // Chi cong cu co lua chon roi rac moi co "phan bo": chon lop, hoac a / b / hoa.
                if (!ToolKinds.CoLop(t.Kind) && t.Kind != ToolKinds.Pairwise)
                {
                    continue;
                }

                SortedDictionary<string, int> dem = new SortedDictionary<string, int>(StringComparer.Ordinal);

                foreach (SampleResult r in ketQua)
                {
                    JsonNode? final = r.Tools[t.Name] == null ? null : r.Tools[t.Name]!["final"];

                    if (t.Kind == ToolKinds.Classification)
                    {
                        if (final != null)
                        {
                            foreach (JsonNode? l in (JsonArray)final["labelIds"]!)
                            {
                                Cong(dem, l!.GetValue<string>());
                            }
                        }
                    }
                    else if (t.Kind == ToolKinds.Pairwise)
                    {
                        if (final != null)
                        {
                            Cong(dem, final["choice"]!.GetValue<string>());
                        }
                    }
                    else if (ToolKinds.LaDanhSach(t.Kind))
                    {
                        foreach (LabelAnnotation a in r.Approved)
                        {
                            JsonArray? muc = a.Payload.KetQua(t.Name) as JsonArray;
                            if (muc == null)
                            {
                                continue;
                            }

                            foreach (JsonNode? m in muc)
                            {
                                Cong(dem, m!["labelId"]!.GetValue<string>());
                            }
                        }
                    }
                }

                ra[t.Name] = dem;
            }

            return ra;
        }

        private static void Cong(SortedDictionary<string, int> dem, string khoa)
        {
            int cu;
            dem.TryGetValue(khoa, out cu);
            dem[khoa] = cu + 1;
        }
    }

    /// <summary>Mot canh bao lech phan bo nhan (FB-24).</summary>
    public sealed class CanhBaoLech
    {
        public CanhBaoLech(string code, string tool, string label, int count, int total, int percent)
        {
            Code = code;
            Tool = tool;
            Label = label;
            Count = count;
            Total = total;
            Percent = percent;
        }

        /// <summary>lop_ap_dao | lop_chua_co_nhan</summary>
        public string Code { get; }

        public string Tool { get; }

        public string Label { get; }

        public int Count { get; }

        public int Total { get; }

        public int Percent { get; }
    }
}
