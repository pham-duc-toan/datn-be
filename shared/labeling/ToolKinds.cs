using System;
using System.Collections.Generic;

namespace Crowd.Labeling
{
    /// <summary>
    /// Cac CONG CU gan nhan va loai du lieu moi cong cu dung duoc.
    ///
    ///   cong cu          | image | text | audio | video | pair | list | ket qua
    ///   -----------------+-------+------+-------+-------+------+------+------------------------------
    ///   classification   |   x   |  x   |   x   |   x   |  x   |  x   | {"labelIds":[...]}
    ///   freeText         |   x   |  x   |   x   |   x   |  x   |  x   | {"text":"..."}  (cau tra loi MO: mo ta, dich...)
    ///   number           |   x   |  x   |   x   |   x   |  x   |  x   | {"value":4}     (thang diem, so dem)
    ///   bbox             |   x   |      |       |       |      |      | [{labelId,x,y,w,h}]
    ///   polygon          |   x   |      |       |       |      |      | [{labelId,points:[[x,y]...]}]
    ///   keypoint         |   x   |      |       |       |      |      | [{labelId,x,y}]
    ///   polyline         |   x   |      |       |       |      |      | [{labelId,points:[[x,y]...]}]  (khong khep kin)
    ///   mask             |   x   |      |       |       |      |      | [{labelId,rle:{size:[h,w],counts:[...]}}]
    ///   span (NER)       |       |  x   |       |       |      |      | [{labelId,start,end}]
    ///   relation         |       |  x   |       |       |      |      | [{labelId,from:{start,end},to:{start,end}}]
    ///   transcription    |   x   |      |   x   |   x   |      |      | {"text":"..."}  (chep lai noi dung CO SAN: loi noi, chu trong anh)
    ///   temporalSegment  |       |      |   x   |   x   |      |      | [{labelId,start,end}] (giay)
    ///   track            |       |      |       |   x   |      |      | [{labelId,keyframes:[{t,x,y,w,h}...]}]
    ///   pairwise         |       |      |       |       |  x   |      | {"choice":"a"|"b"|"tie"}
    ///   ranking          |       |      |       |       |      |  x   | {"order":[2,0,1]}
    ///
    /// Cong cu dang DANH SACH (bbox, polygon, keypoint, polyline, mask, span,
    /// temporalSegment, track) cho phep moi doi tuong mang THUOC TINH rieng
    /// ("attributes" trong tap nhan) — vd OCR = bbox + thuoc tinh text.
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
        public const string FreeText = "freeText";
        public const string Number = "number";
        public const string Keypoint = "keypoint";
        public const string Polyline = "polyline";
        public const string Mask = "mask";
        public const string Track = "track";
        public const string Relation = "relation";
        public const string Ranking = "ranking";

        private static readonly Dictionary<string, string[]> DungDuocCho = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            { Classification, new string[] { Modalities.Image, Modalities.Text, Modalities.Audio, Modalities.Video, Modalities.Pair, Modalities.List } },
            { FreeText, new string[] { Modalities.Image, Modalities.Text, Modalities.Audio, Modalities.Video, Modalities.Pair, Modalities.List } },
            { Number, new string[] { Modalities.Image, Modalities.Text, Modalities.Audio, Modalities.Video, Modalities.Pair, Modalities.List } },
            { Bbox, new string[] { Modalities.Image } },
            { Polygon, new string[] { Modalities.Image } },
            { Keypoint, new string[] { Modalities.Image } },
            { Polyline, new string[] { Modalities.Image } },
            { Mask, new string[] { Modalities.Image } },
            { Span, new string[] { Modalities.Text } },
            { Relation, new string[] { Modalities.Text } },
            { Transcription, new string[] { Modalities.Image, Modalities.Audio, Modalities.Video } },
            { TemporalSegment, new string[] { Modalities.Audio, Modalities.Video } },
            { Track, new string[] { Modalities.Video } },
            { Pairwise, new string[] { Modalities.Pair } },
            { Ranking, new string[] { Modalities.List } },
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
            return kind == Bbox || kind == Polygon || kind == Span || kind == TemporalSegment
                   || kind == Keypoint || kind == Polyline || kind == Mask || kind == Track || kind == Relation;
        }

        /// <summary>Cong cu ma moi doi tuong mang duoc thuoc tinh rieng ("attributes").</summary>
        public static bool CoThuocTinh(string kind)
        {
            return LaDanhSach(kind) && kind != Relation;
        }

        /// <summary>Cong cu chon lop (co "classes").</summary>
        public static bool CoLop(string kind)
        {
            return kind == Classification || LaDanhSach(kind);
        }
    }
}
