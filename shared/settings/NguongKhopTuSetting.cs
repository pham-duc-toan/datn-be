using System;
using System.Collections.Generic;
using Crowd.BuildingBlocks.Settings;
using Crowd.Labeling;

namespace Crowd.Settings
{
    /// <summary>
    /// Setting labeling.threshold.* -&gt; nguong mac dinh theo loai cong cu (cham dap an va ghep doi tuong khi gop).
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
            theoLoai[ToolKinds.Keypoint] = settings.SoThuc(SettingKeys.LabelingThresholdKeypoint);
            theoLoai[ToolKinds.Polyline] = settings.SoThuc(SettingKeys.LabelingThresholdPolyline);
            theoLoai[ToolKinds.Mask] = settings.SoThuc(SettingKeys.LabelingThresholdMask);
            theoLoai[ToolKinds.Track] = settings.SoThuc(SettingKeys.LabelingThresholdTrack);
            theoLoai[ToolKinds.Relation] = settings.SoThuc(SettingKeys.LabelingThresholdRelation);
            theoLoai[ToolKinds.Ranking] = settings.SoThuc(SettingKeys.LabelingThresholdRanking);
            return new NguongKhop(theoLoai);
        }
    }
}
