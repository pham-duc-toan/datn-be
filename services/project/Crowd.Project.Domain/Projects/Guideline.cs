using System;
using System.Collections.Generic;
using Crowd.Project.Domain.Common;

namespace Crowd.Project.Domain.Projects
{
    /// <summary>
    /// Huong dan gan nhan kem vi du dung/sai (FB-13). Bat bien nhu LabelSchema.
    /// </summary>
    public sealed class Guideline
    {

        private readonly List<GuidelineExample> _examples;

        private Guideline(string markdown, List<GuidelineExample> examples)
        {
            Markdown = markdown;
            _examples = examples;
        }

        /// <summary>Noi dung huong dan dang Markdown — frontend tu hien thi.</summary>
        public string Markdown { get; }

        public IReadOnlyList<GuidelineExample> Examples
        {
            get { return _examples; }
        }

        public static Guideline Tao(string markdown, IEnumerable<GuidelineExample>? examples, QuyDinhDuAn quyDinh)
        {
            if (quyDinh == null)
            {
                throw new ArgumentNullException(nameof(quyDinh));
            }

            if (string.IsNullOrWhiteSpace(markdown) || markdown.Length > quyDinh.DoDaiHuongDanToiDa)
            {
                throw new InvalidValueException(
                    "huong_dan_khong_hop_le",
                    "Huong dan phai tu 1 den " + quyDinh.DoDaiHuongDanToiDa + " ky tu.");
            }

            List<GuidelineExample> danhSach = new List<GuidelineExample>();
            if (examples != null)
            {
                danhSach.AddRange(examples);
            }

            if (danhSach.Count > quyDinh.SoViDuToiDa)
            {
                throw new InvalidValueException(
                    "qua_nhieu_vi_du",
                    "Toi da " + quyDinh.SoViDuToiDa + " vi du.");
            }

            foreach (GuidelineExample vd in danhSach)
            {
                if (vd.Explanation.Length > quyDinh.DoDaiGiaiThichToiDa)
                {
                    throw new InvalidValueException(
                        "giai_thich_khong_hop_le",
                        "Moi vi du can giai thich tu 1 den " + quyDinh.DoDaiGiaiThichToiDa + " ky tu.");
                }
            }

            return new Guideline(markdown, danhSach);
        }

        /// <summary>Doc lai tu DB — KHONG kiem gioi han hien tai (gioi han chi ap cho thao tac moi).</summary>
        public static Guideline TuLuuTru(string markdown, IEnumerable<GuidelineExample>? examples)
        {
            List<GuidelineExample> danhSach = new List<GuidelineExample>();
            if (examples != null)
            {
                danhSach.AddRange(examples);
            }

            return new Guideline(markdown, danhSach);
        }
    }

    /// <summary>Mot vi du trong huong dan: "mau nay gan nhan X la DUNG/SAI vi ...".</summary>
    public sealed class GuidelineExample
    {
        public GuidelineExample(Guid? sampleId, string? label, bool isCorrect, string explanation)
        {
            // Do dai toi da kiem o Guideline.Tao theo quy dinh hien tai.
            if (string.IsNullOrWhiteSpace(explanation))
            {
                throw new InvalidValueException("giai_thich_khong_hop_le", "Moi vi du can giai thich.");
            }

            SampleId = sampleId;
            Label = label;
            IsCorrect = isCorrect;
            Explanation = explanation.Trim();
        }

        /// <summary>Mau minh hoa (neu co) — mot mau da nap trong dataset cua du an.</summary>
        public Guid? SampleId { get; }

        /// <summary>Nhan duoc noi toi trong vi du. Phai la lop co trong schema.</summary>
        public string? Label { get; }

        /// <summary>true = vi du DUNG, false = vi du SAI thuong gap.</summary>
        public bool IsCorrect { get; }

        public string Explanation { get; }
    }
}
