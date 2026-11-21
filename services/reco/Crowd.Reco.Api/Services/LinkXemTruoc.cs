using System;
using System.Collections.Concurrent;
using System.Linq;
using Crowd.BuildingBlocks.Storage;

namespace Crowd.Reco.Api.Services
{
    /// <summary>
    /// Giu link ban xem truoc DUNG CHUNG theo (mau, ngay UTC) — kien truc 3.40. Token co nonce ngau nhien nen tao moi
    /// moi lan la moi lan mot link khac → trinh duyet / CDN khong dung lai duoc; giu mot link moi mau moi ngay thi ai luot
    /// cung tai cung mot URL (Cache-Control public o media-svc). Sang ngay moi: link moi, link cu don di.
    /// Moi instance reco mot bo — vai instance thi vai link moi ngay, van cache duoc.
    /// </summary>
    public sealed class LinkXemTruoc
    {
        private readonly IMediaLinks _media;
        private readonly TimeProvider _clock;
        private readonly ConcurrentDictionary<string, string?> _link = new ConcurrentDictionary<string, string?>();
        private DateTime _ngay;

        public LinkXemTruoc(IMediaLinks media, TimeProvider clock)
        {
            if (media == null)
            {
                throw new ArgumentNullException(nameof(media));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _media = media;
            _clock = clock;
        }

        public string? Lay(MauXemTruocRam m, string? tenDoanhNghiep)
        {
            if (m == null)
            {
                throw new ArgumentNullException(nameof(m));
            }

            if (m.StorageKey == null)
            {
                return null;
            }

            DateTime ngay = _clock.GetUtcNow().UtcDateTime.Date;
            if (ngay != _ngay)
            {
                _ngay = ngay;
                string tienTo = ngay.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) + "|";
                foreach (string k in _link.Keys.Where(k => !k.StartsWith(tienTo, StringComparison.Ordinal)).ToList())
                {
                    string? bo;
                    _link.TryRemove(k, out bo);
                }
            }

            string khoa = ngay.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) + "|" + m.SampleId.ToString("N")
                + "|" + (tenDoanhNghiep ?? string.Empty);
            return _link.GetOrAdd(khoa, delegate
            {
                MauCanXem mx = new MauCanXem
                {
                    SampleId = m.SampleId,
                    ProjectId = m.ProjectId,
                    StorageKey = m.StorageKey,
                    Modality = m.Modality,
                    TenDoanhNghiep = tenDoanhNghiep,
                    BatDau = m.BatDau,
                    KetThuc = m.KetThuc,
                };
                return _media.TaoLinkXemTruoc(mx);
            });
        }
    }
}
