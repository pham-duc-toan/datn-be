using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Crowd.BuildingBlocks.Settings
{
    /// <summary>
    /// Cho mot chu ky worker lay tu setting (vd ledger.hold_release_interval), nhung theo
    /// NHIP ngan va DOC LAI setting moi nhip.
    ///
    /// Mot Task.Delay(chu ky) duy nhat dung gia tri doc LUC BAT DAU cho: admin rut chu ky
    /// tu 60 giay xuong 5 giay thi worker van ngu not 60 giay cu; vua khoi dong ma ban sao
    /// setting chua dong bo thi chu ky dau la gia tri MAC DINH (gap khi do NC-B-06 va khi
    /// chay E2E tong the). Doc lai moi nhip: chu ky moi co hieu luc trong toi da mot nhip.
    /// Keo dai chu ky cung co hieu luc ngay (worker cho them).
    /// </summary>
    public static class ChoTheoSetting
    {
        public static readonly TimeSpan NhipMacDinh = TimeSpan.FromSeconds(1);

        public static Task ChoAsync(ISettings settings, string key, CancellationToken ct)
        {
            return ChoAsync(settings, key, NhipMacDinh, ct);
        }

        public static async Task ChoAsync(ISettings settings, string key, TimeSpan nhip, CancellationToken ct)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (nhip <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(nhip), "Nhip cho phai lon hon 0.");
            }

            Stopwatch daCho = Stopwatch.StartNew();
            while (true)
            {
                TimeSpan conLai = settings.ThoiGian(key) - daCho.Elapsed;
                if (conLai <= TimeSpan.Zero)
                {
                    return;
                }

                TimeSpan lanNay = conLai < nhip ? conLai : nhip;
                await Task.Delay(lanNay, ct).ConfigureAwait(false);
            }
        }
    }
}
