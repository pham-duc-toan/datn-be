using System;
using Crowd.Annotation.Domain.Annotations;
using Crowd.BuildingBlocks.Settings;

namespace Crowd.Annotation.Api.Helpers
{
    /// <summary>Doc setting hien tai thanh quy dinh truyen vao domain. Goi MOI lan can.</summary>
    public static class QuyDinhTuSetting
    {
        public static QuyDinhDuyetNhan DuyetNhan(ISettings s)
        {
            if (s == null)
            {
                throw new ArgumentNullException(nameof(s));
            }

            return new QuyDinhDuyetNhan
            {
                DoDaiLyDoToiDa = Math.Min(s.SoNguyen(SettingKeys.AnnotationReasonMaxLength), LabelAnnotation.CotDbLyDo),
                HanKhieuNai = s.ThoiGian(SettingKeys.AnnotationAppealWindow),
            };
        }
    }
}
