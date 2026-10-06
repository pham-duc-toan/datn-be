using System;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Crowd.Labeling
{
    /// <summary>
    /// Thong tin cua MOT mau, dung de kiem nhan co nam trong gioi han cua mau
    /// khong (khung trong anh, doan van trong van ban, doan thoi gian trong file).
    ///
    /// Luu thanh cot metadata jsonb. Truong nao khong ap dung cho loai du lieu
    /// thi null:
    ///
    ///   image  {"width":1920,"height":1080}
    ///   text   {"length":245}
    ///   audio  {"durationSec":30}
    ///   video  {"durationSec":30,"width":1280,"height":720}
    ///   doan cat tu file dai (audio/video):
    ///          {"durationSec":30,"segmentStart":60,"segmentEnd":90,"sourceDurationSec":300}
    ///
    /// Thoi gian cua nhan (temporalSegment) tinh tu DAU MAU (0..durationSec), khong
    /// tinh tu dau file goc — giao dien chi can phat tu segmentStart.
    /// </summary>
    public sealed class SampleMetadata
    {
        public int? Width { get; init; }

        public int? Height { get; init; }

        public double? DurationSec { get; init; }

        /// <summary>So ky tu cua van ban (text).</summary>
        public int? Length { get; init; }

        public double? SegmentStart { get; init; }

        public double? SegmentEnd { get; init; }

        public double? SourceDurationSec { get; init; }

        public static readonly SampleMetadata Rong = new SampleMetadata();

        public RawJson ToRawJson()
        {
            JsonObject o = new JsonObject();
            if (Width.HasValue)
            {
                o["width"] = Width.Value;
            }

            if (Height.HasValue)
            {
                o["height"] = Height.Value;
            }

            if (DurationSec.HasValue)
            {
                o["durationSec"] = Math.Round(DurationSec.Value, 3);
            }

            if (Length.HasValue)
            {
                o["length"] = Length.Value;
            }

            if (SegmentStart.HasValue)
            {
                o["segmentStart"] = Math.Round(SegmentStart.Value, 3);
            }

            if (SegmentEnd.HasValue)
            {
                o["segmentEnd"] = Math.Round(SegmentEnd.Value, 3);
            }

            if (SourceDurationSec.HasValue)
            {
                o["sourceDurationSec"] = Math.Round(SourceDurationSec.Value, 3);
            }

            return RawJson.Tu(o);
        }

        public static SampleMetadata Tu(RawJson? json)
        {
            if (json == null)
            {
                return Rong;
            }

            JsonObject? o = json.Node() as JsonObject;
            if (o == null)
            {
                throw new LabelFormatException("Metadata mau phai la object.");
            }

            return new SampleMetadata
            {
                Width = SoNguyen(o, "width"),
                Height = SoNguyen(o, "height"),
                DurationSec = SoThuc(o, "durationSec"),
                Length = SoNguyen(o, "length"),
                SegmentStart = SoThuc(o, "segmentStart"),
                SegmentEnd = SoThuc(o, "segmentEnd"),
                SourceDurationSec = SoThuc(o, "sourceDurationSec"),
            };
        }

        public override string ToString()
        {
            return ToRawJson().Json;
        }

        private static int? SoNguyen(JsonObject o, string ten)
        {
            JsonNode? n = o[ten];
            if (n == null)
            {
                return null;
            }

            return (int)Math.Round(n.GetValue<double>(), MidpointRounding.AwayFromZero);
        }

        private static double? SoThuc(JsonObject o, string ten)
        {
            JsonNode? n = o[ten];
            if (n == null)
            {
                return null;
            }

            return double.Parse(n.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}
