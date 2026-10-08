using System;
using Crowd.BuildingBlocks.Settings;

namespace Crowd.BuildingBlocks.Persistence.Outbox
{
    /// <summary>Danh dau "service khong dang ky ISettings" de khong tim lai moi vong.</summary>
    internal sealed class KhongCoSetting : ISettings
    {
        public static readonly KhongCoSetting Instance = new KhongCoSetting();

        private KhongCoSetting()
        {
        }

        public bool DungSai(string key)
        {
            throw new NotSupportedException();
        }

        public int SoNguyen(string key)
        {
            throw new NotSupportedException();
        }

        public long SoLon(string key)
        {
            throw new NotSupportedException();
        }

        public double SoThuc(string key)
        {
            throw new NotSupportedException();
        }

        public TimeSpan ThoiGian(string key)
        {
            throw new NotSupportedException();
        }

        public string Chuoi(string key)
        {
            throw new NotSupportedException();
        }
    }
}
