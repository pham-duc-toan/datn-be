using System;
using Crowd.BuildingBlocks.Settings;
using Crowd.Project.Domain.Common;
using Crowd.Project.Infrastructure.Datasets;

namespace Crowd.Project.Api.Helpers
{
    /// <summary>
    /// Doc setting hien tai thanh cac "quy dinh" truyen vao domain. Goi MOI lan can
    /// (khong giu lau) — admin doi setting la thao tac tiep theo dung gia tri moi.
    /// </summary>
    public static class QuyDinhTuSetting
    {
        public static QuyDinhDuAn DuAn(ISettings s)
        {
            if (s == null)
            {
                throw new ArgumentNullException(nameof(s));
            }

            return new QuyDinhDuAn
            {
                DoDaiTenToiDa = Math.Min(s.SoNguyen(SettingKeys.ProjectNameMaxLength), CotDb.TenDuAn),
                DoDaiMoTaToiDa = Math.Min(s.SoNguyen(SettingKeys.ProjectDescriptionMaxLength), CotDb.MoTaDuAn),
                RedundancyToiDa = s.SoNguyen(SettingKeys.ProjectRedundancyMax),
                GoldCheckPercentMacDinh = s.SoNguyen(SettingKeys.ProjectGoldCheckPercentDefault),
                GoldCheckPercentToiDa = s.SoNguyen(SettingKeys.ProjectGoldCheckPercentMax),
                SoCauTestMacDinh = s.SoNguyen(SettingKeys.ProjectEntranceQuestionDefault),
                NguongDauMacDinh = s.SoNguyen(SettingKeys.ProjectEntrancePassPercentDefault),
                SoCauTestToiDa = s.SoNguyen(SettingKeys.ProjectEntranceQuestionMax),
                DoDaiHuongDanToiDa = s.SoNguyen(SettingKeys.GuidelineMarkdownMaxLength),
                SoViDuToiDa = s.SoNguyen(SettingKeys.GuidelineExamplesMax),
                DoDaiGiaiThichToiDa = s.SoNguyen(SettingKeys.GuidelineExplanationMaxLength),
            };
        }

        public static QuyDinhDuLieu DuLieu(ISettings s)
        {
            if (s == null)
            {
                throw new ArgumentNullException(nameof(s));
            }

            return new QuyDinhDuLieu
            {
                DoDaiTenLoToiDa = Math.Min(s.SoNguyen(SettingKeys.DatasetNameMaxLength), CotDb.TenLo),
                DoDaiLoiToiDa = Math.Min(s.SoNguyen(SettingKeys.DatasetErrorSummaryMaxLength), CotDb.TomTatLoi),
                DoDaiTenMauToiDa = Math.Min(s.SoNguyen(SettingKeys.SampleNameMaxLength), CotDb.TenMau),
            };
        }

        public static QuyDinhBaiTest BaiTest(ISettings s)
        {
            if (s == null)
            {
                throw new ArgumentNullException(nameof(s));
            }

            return new QuyDinhBaiTest
            {
                SoLanToiDa = s.SoNguyen(SettingKeys.EntranceMaxAttempts),
                ThoiGianLamBai = s.ThoiGian(SettingKeys.EntranceDuration),
            };
        }

        public static ZipLimits Zip(ISettings s)
        {
            if (s == null)
            {
                throw new ArgumentNullException(nameof(s));
            }

            return new ZipLimits
            {
                MaxEntries = s.SoNguyen(SettingKeys.DatasetZipMaxEntries),
                MaxImageBytes = s.SoLon(SettingKeys.DatasetZipImageMaxBytes),
                MaxTotalBytes = s.SoLon(SettingKeys.DatasetZipTotalMaxBytes),
            };
        }
    }
}
