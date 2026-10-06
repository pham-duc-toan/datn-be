using System;
using System.Collections.Generic;

namespace Crowd.Labeling
{
    /// <summary>
    /// LOAI DU LIEU cua mot du an — moi mau (sample) cua du an deu thuoc loai nay.
    ///
    ///   image  mot anh                       metadata: width, height
    ///   text   mot doan van ban              content: {"text"}, metadata: length
    ///   audio  mot file am thanh (hoac doan) metadata: durationSec (+ segment)
    ///   video  mot file video (hoac doan)    metadata: durationSec, width, height (+ segment)
    ///   pair   mot cap noi dung can so sanh  content: {"prompt","a","b"} — vd hai cau tra loi LLM
    /// </summary>
    public static class Modalities
    {
        public const string Image = "image";
        public const string Text = "text";
        public const string Audio = "audio";
        public const string Video = "video";
        public const string Pair = "pair";

        public static readonly IReadOnlyList<string> TatCa = new string[] { Image, Text, Audio, Video, Pair };

        public static bool HopLe(string? modality)
        {
            foreach (string m in TatCa)
            {
                if (string.Equals(m, modality, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Du lieu la FILE (luu MinIO) hay NOI DUNG (luu thang trong database).</summary>
        public static bool LaFile(string modality)
        {
            return modality == Image || modality == Audio || modality == Video;
        }

        /// <summary>Du lieu co truc thoi gian — cat doan duoc (segmentSeconds).</summary>
        public static bool CoThoiGian(string modality)
        {
            return modality == Audio || modality == Video;
        }
    }
}
