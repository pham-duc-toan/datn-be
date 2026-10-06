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
        ///   {"loai":{"kind":"classification","method":"majority","final":{...},"votes":{...},"disputed":false},
        ///    "vat":{"kind":"bbox","method":"none"}}
        /// "none" = chua co cach gop tu dong (khung, van ban chep...) — ket qua cuoi
        /// la cac nhan DA DUYET (Approved).
        /// </summary>
        public JsonObject Tools { get; }

        /// <summary>Cac nhan da duyet cua mau, cu nhat truoc.</summary>
        public IReadOnlyList<LabelAnnotation> Approved { get; }

        /// <summary>Co cong cu nao gop theo da so ma khong lua chon nao qua ban (FB-22).</summary>
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
    ///   bbox / polygon / span / temporalSegment / transcription → "none": chua gop
    ///       tu dong (hai khung lech vai pixel van la mot vat — can IoU + ghep cap);
    ///       ket qua cuoi la cac nhan da duyet. quality-svc (P3) se thay bang
    ///       Dawid–Skene / WBF / ROVER co trong so theo do tin cay tung labeler.
    /// </summary>
    public static class ResultAggregator
    {
        public static IReadOnlyList<SampleResult> Chot(LabelSchema tapNhan, IEnumerable<LabelAnnotation> daDuyet)
        {
            if (tapNhan == null)
            {
                throw new ArgumentNullException(nameof(tapNhan));
            }

            if (daDuyet == null)
            {
                throw new ArgumentNullException(nameof(daDuyet));
            }

            List<SampleResult> ketQua = new List<SampleResult>();

            foreach (IGrouping<Guid, LabelAnnotation> nhom in daDuyet.GroupBy(a => a.SampleId).OrderBy(g => g.Key))
            {
                List<LabelAnnotation> ds = nhom.OrderBy(a => a.SubmittedAt).ToList();
                List<LabelPayload> nhan = ds.Select(a => a.Payload).ToList();

                ketQua.Add(new SampleResult(nhom.Key, ds.Count, LabelAggregator.Gop(tapNhan, nhan), ds));
            }

            return ketQua;
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
                if (t.Kind == ToolKinds.Transcription)
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
}
