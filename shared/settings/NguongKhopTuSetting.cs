using System;
using System.Collections.Generic;
using Crowd.BuildingBlocks.Settings;
using Crowd.Labeling;

namespace Crowd.Settings
{
    /// <summary>
    /// Setting labeling.threshold.* -&gt; nguong cham mac dinh theo loai cong cu.
    /// Goi MOI lan cham (khong giu lau) de admin doi la lan cham sau dung gia tri moi.
    /// </summary>
    public static class NguongKhopTuSetting
    {
        public static NguongKhop Doc(ISettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            Dictionary<string, double> theoLoai = new Dictionary<string, double>(StringComparer.Ordinal);
            theoLoai[ToolKinds.Bbox] = settings.SoThuc(SettingKeys.LabelingThresholdBbox);
            theoLoai[ToolKinds.Polygon] = settings.SoThuc(SettingKeys.LabelingThresholdPolygon);
            theoLoai[ToolKinds.Span] = settings.SoThuc(SettingKeys.LabelingThresholdSpan);
            theoLoai[ToolKinds.Transcription] = settings.SoThuc(SettingKeys.LabelingThresholdTranscription);
            theoLoai[ToolKinds.TemporalSegment] = settings.SoThuc(SettingKeys.LabelingThresholdTemporalSegment);
            return new NguongKhop(theoLoai);
        }
    }
}
