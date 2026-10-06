using System;
using System.Collections.Generic;

namespace Crowd.Labeling
{
    /// <summary>
    /// Cac CONG CU gan nhan va loai du lieu moi cong cu dung duoc.
    ///
    ///   cong cu          | image | text | audio | video | pair | ket qua
    ///   -----------------+-------+------+-------+-------+------+------------------------------
    ///   classification   |   x   |  x   |   x   |   x   |  x   | {"labelIds":[...]}
    ///   bbox             |   x   |      |       |       |      | [{labelId,x,y,w,h}]
    ///   polygon          |   x   |      |       |       |      | [{labelId,points:[[x,y]...]}]
    ///   span (NER)       |       |  x   |       |       |      | [{labelId,start,end}]
    ///   transcription    |       |      |   x   |   x   |      | {"text":"..."}
    ///   temporalSegment  |       |      |   x   |   x   |      | [{labelId,start,end}] (giay)
    ///   pairwise         |       |      |       |       |  x   | {"choice":"a"|"b"|"tie"}
    ///
    /// Them cong cu moi: them mot dong o day, mot file contracts/labeling/tools/
    /// &lt;kind&gt;.result.schema.json, va mot lop trong thu muc Tools/.
    /// </summary>
    public static class ToolKinds
    {
        public const string Classification = "classification";
        public const string Bbox = "bbox";
        public const string Polygon = "polygon";
        public const string Span = "span";
        public const string Transcription = "transcription";
        public const string TemporalSegment = "temporalSegment";
        public const string Pairwise = "pairwise";

        private static readonly Dictionary<string, string[]> DungDuocCho = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            { Classification, new string[] { Modalities.Image, Modalities.Text, Modalities.Audio, Modalities.Video, Modalities.Pair } },
            { Bbox, new string[] { Modalities.Image } },
            { Polygon, new string[] { Modalities.Image } },
            { Span, new string[] { Modalities.Text } },
            { Transcription, new string[] { Modalities.Audio, Modalities.Video } },
            { TemporalSegment, new string[] { Modalities.Audio, Modalities.Video } },
            { Pairwise, new string[] { Modalities.Pair } },
        };

        public static IReadOnlyCollection<string> TatCa
        {
            get { return DungDuocCho.Keys; }
        }

        public static bool HopVoi(string kind, string modality)
        {
            string[]? ds;
            if (!DungDuocCho.TryGetValue(kind, out ds))
            {
                return false;
            }

            return Array.IndexOf(ds, modality) >= 0;
        }

        /// <summary>Ket qua la DANH SACH doi tuong (co minItems / maxItems).</summary>
        public static bool LaDanhSach(string kind)
        {
            return kind == Bbox || kind == Polygon || kind == Span || kind == TemporalSegment;
        }

        /// <summary>Cong cu chon lop (co "classes").</summary>
        public static bool CoLop(string kind)
        {
            return kind == Classification || LaDanhSach(kind);
        }
    }
}
