using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Settings;

namespace Crowd.BuildingBlocks.Tests.Settings
{
    public sealed class ChoTheoSettingTests
    {
        private const string Khoa = "worker.interval";

        /// <summary>Setting gia: chu ky doi duoc giua chung.</summary>
        private sealed class SettingGia : ISettings
        {
            private long _ticks;

            public SettingGia(TimeSpan chuKy)
            {
                _ticks = chuKy.Ticks;
            }

            public void Doi(TimeSpan chuKy)
            {
                Interlocked.Exchange(ref _ticks, chuKy.Ticks);
            }

            public TimeSpan ThoiGian(string key)
            {
                return TimeSpan.FromTicks(Interlocked.Read(ref _ticks));
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

            public string Chuoi(string key)
            {
                throw new NotSupportedException();
            }
        }

        [Fact]
        public async Task Cho_du_chu_ky_khi_setting_khong_doi()
        {
            SettingGia s = new SettingGia(TimeSpan.FromMilliseconds(300));
            Stopwatch dh = Stopwatch.StartNew();

            await ChoTheoSetting.ChoAsync(s, Khoa, TimeSpan.FromMilliseconds(50), CancellationToken.None);

            Assert.True(dh.Elapsed >= TimeSpan.FromMilliseconds(290), dh.Elapsed.ToString());
        }

        [Fact]
        public async Task Rut_ngan_chu_ky_giua_chung_thi_ket_thuc_som()
        {
            // Bat dau cho 30 giay; 200 ms sau admin doi xuong 100 ms → phai xong ngay o nhip ke.
            SettingGia s = new SettingGia(TimeSpan.FromSeconds(30));
            Stopwatch dh = Stopwatch.StartNew();

            Task cho = ChoTheoSetting.ChoAsync(s, Khoa, TimeSpan.FromMilliseconds(50), CancellationToken.None);
            await Task.Delay(200);
            s.Doi(TimeSpan.FromMilliseconds(100));
            await cho;

            Assert.True(dh.Elapsed < TimeSpan.FromSeconds(5), dh.Elapsed.ToString());
        }

        [Fact]
        public async Task Keo_dai_chu_ky_giua_chung_thi_cho_them()
        {
            SettingGia s = new SettingGia(TimeSpan.FromMilliseconds(200));
            Stopwatch dh = Stopwatch.StartNew();

            Task cho = ChoTheoSetting.ChoAsync(s, Khoa, TimeSpan.FromMilliseconds(50), CancellationToken.None);
            await Task.Delay(50);
            s.Doi(TimeSpan.FromMilliseconds(600));
            await cho;

            Assert.True(dh.Elapsed >= TimeSpan.FromMilliseconds(590), dh.Elapsed.ToString());
        }

        [Fact]
        public async Task Huy_thi_nem_OperationCanceled()
        {
            SettingGia s = new SettingGia(TimeSpan.FromSeconds(30));
            CancellationTokenSource huy = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => ChoTheoSetting.ChoAsync(s, Khoa, TimeSpan.FromMilliseconds(50), huy.Token));
        }
    }
}
