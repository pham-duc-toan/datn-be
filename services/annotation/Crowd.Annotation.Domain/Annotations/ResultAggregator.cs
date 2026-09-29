using System;
using System.Collections.Generic;
using System.Linq;

namespace Crowd.Annotation.Domain.Annotations
{
    /// <summary>Ket qua chot cho mot mau.</summary>
    public sealed class SampleResult
    {
        public SampleResult(Guid sampleId, IReadOnlyList<string> finalLabels, IReadOnlyDictionary<string, int> votes, int approvedCount)
        {
            SampleId = sampleId;
            FinalLabels = finalLabels;
            Votes = votes;
            ApprovedCount = approvedCount;
        }

        public Guid SampleId { get; }

        /// <summary>Rong = tranh chap (khong nhan nao qua ban).</summary>
        public IReadOnlyList<string> FinalLabels { get; }

        /// <summary>Moi lop duoc bao nhieu nguoi (da duyet) chon.</summary>
        public IReadOnlyDictionary<string, int> Votes { get; }

        public int ApprovedCount { get; }

        public bool Disputed
        {
            get { return FinalLabels.Count == 0; }
        }
    }

    /// <summary>
    /// Chot nhan cuoi cho moi mau tu cac nhan DA DUYET, bang DA SO TUYET DOI:
    /// mot lop duoc chon neu hon MOT NUA so nguoi chon no.
    ///
    ///   3 nguoi: cho, cho, meo  → cho (2/3 > 1/2)
    ///   2 nguoi: cho, meo       → tranh chap (1/2 khong qua ban) — FB-22
    ///
    /// Cung luat cho multi-label: moi lop xet doc lap. Day la luat don gian co the
    /// giai thich duoc; quality-svc (P3) se thay bang Dawid–Skene co trong so theo
    /// do tin cay tung labeler (FQ-01).
    /// </summary>
    public static class ResultAggregator
    {
        public static IReadOnlyList<SampleResult> Chot(IEnumerable<LabelAnnotation> daDuyet)
        {
            if (daDuyet == null)
            {
                throw new ArgumentNullException(nameof(daDuyet));
            }

            List<SampleResult> ketQua = new List<SampleResult>();

            foreach (IGrouping<Guid, LabelAnnotation> nhom in daDuyet.GroupBy(a => a.SampleId).OrderBy(g => g.Key))
            {
                int soNguoi = nhom.Count();
                SortedDictionary<string, int> phieu = new SortedDictionary<string, int>(StringComparer.Ordinal);

                foreach (LabelAnnotation a in nhom)
                {
                    foreach (string lop in a.Labels.Distinct(StringComparer.Ordinal))
                    {
                        int cu;
                        phieu.TryGetValue(lop, out cu);
                        phieu[lop] = cu + 1;
                    }
                }

                List<string> chot = new List<string>();
                foreach (KeyValuePair<string, int> p in phieu)
                {
                    if (p.Value * 2 > soNguoi)
                    {
                        chot.Add(p.Key);
                    }
                }

                ketQua.Add(new SampleResult(nhom.Key, chot, phieu, soNguoi));
            }

            return ketQua;
        }
    }
}
