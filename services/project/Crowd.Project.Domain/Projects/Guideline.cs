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
        public const int DoDaiNoiDungToiDa = 20000;
        public const int SoViDuToiDa = 50;

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

        public static Guideline Tao(string markdown, IEnumerable<GuidelineExample>? examples)
        {
            if (string.IsNullOrWhiteSpace(markdown) || markdown.Length > DoDaiNoiDungToiDa)
            {
                throw new InvalidValueException(
                    "huong_dan_khong_hop_le",
                    "Huong dan phai tu 1 den " + DoDaiNoiDungToiDa + " ky tu.");
            }

            List<GuidelineExample> danhSach = new List<GuidelineExample>();
            if (examples != null)
            {
                danhSach.AddRange(examples);
            }

            if (danhSach.Count > SoViDuToiDa)
            {
                throw new InvalidValueException(
                    "qua_nhieu_vi_du",
                    "Toi da " + SoViDuToiDa + " vi du.");
            }

            return new Guideline(markdown, danhSach);
        }
    }

    /// <summary>Mot vi du trong huong dan: "mau nay gan nhan X la DUNG/SAI vi ...".</summary>
    public sealed class GuidelineExample
    {
        public const int DoDaiGiaiThichToiDa = 1000;

        public GuidelineExample(Guid? sampleId, string? label, bool isCorrect, string explanation)
        {
            if (string.IsNullOrWhiteSpace(explanation) || explanation.Length > DoDaiGiaiThichToiDa)
            {
                throw new InvalidValueException(
                    "giai_thich_khong_hop_le",
                    "Moi vi du can giai thich tu 1 den " + DoDaiGiaiThichToiDa + " ky tu.");
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
