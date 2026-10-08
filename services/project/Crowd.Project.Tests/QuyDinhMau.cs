using Crowd.Project.Api.Helpers;
using Crowd.Project.Domain.Common;
using Crowd.Settings;

namespace Crowd.Project.Tests
{
    /// <summary>Quy dinh dung gia tri MAC DINH cua catalog setting (store rong).</summary>
    internal static class QuyDinhMau
    {
        private static readonly SettingsStore MacDinh = new SettingsStore();

        public static QuyDinhDuAn DuAn
        {
            get { return QuyDinhTuSetting.DuAn(MacDinh); }
        }

        public static QuyDinhDuLieu DuLieu
        {
            get { return QuyDinhTuSetting.DuLieu(MacDinh); }
        }

        public static QuyDinhBaiTest BaiTest
        {
            get { return QuyDinhTuSetting.BaiTest(MacDinh); }
        }
    }
}
