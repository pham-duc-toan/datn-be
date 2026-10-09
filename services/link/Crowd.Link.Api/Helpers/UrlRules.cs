using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Crowd.Link.Api.Entities;
using Crowd.Link.Api.Exceptions;

namespace Crowd.Link.Api.Helpers
{
    /// <summary>Link dich da chuan hoa.</summary>
    public sealed class UrlChuan
    {
        public UrlChuan(string url, string tenMien)
        {
            Url = url;
            TenMien = tenMien;
        }

        public string Url { get; }

        /// <summary>Chu thuong, punycode, khong dau cham cuoi.</summary>
        public string TenMien { get; }
    }

    /// <summary>Luat cho link dich, alias va ma ngau nhien.</summary>
    public static class UrlRules
    {
        private const string BangChu = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

        private static readonly Regex MauAlias = new Regex("^[A-Za-z0-9_-]{3,32}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>Kiem va chuan hoa link dich. Tra ve URL tuyet doi + ten mien (chu thuong, punycode).</summary>
        public static UrlChuan ChuanHoa(string? url)
        {
            string s = url == null ? string.Empty : url.Trim();
            if (s.Length == 0 || s.Length > ShortLink.CotUrl)
            {
                throw new LinkException(400, "url_khong_hop_le", "Link dich phai co 1-" + ShortLink.CotUrl + " ky tu.");
            }

            Uri? uri;
            if (!Uri.TryCreate(s, UriKind.Absolute, out uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new LinkException(400, "url_khong_hop_le", "Link dich phai la dia chi http(s) day du.");
            }

            if (uri.UserInfo.Length > 0)
            {
                // "https://google.com@evil.com" — mot kieu gia mao ten mien quen thuoc.
                throw new LinkException(400, "url_khong_hop_le", "Link dich khong duoc chua thong tin dang nhap.");
            }

            string tenMien = uri.IdnHost.ToLowerInvariant().TrimEnd('.');
            if (tenMien.Length == 0 || tenMien.Length > ShortLink.CotTenMien || tenMien == "localhost")
            {
                throw new LinkException(400, "url_khong_hop_le", "Ten mien cua link dich khong hop le.");
            }

            IPAddress? ip;
            if (IPAddress.TryParse(tenMien.Trim('[', ']'), out ip) && LaDiaChiNoiBo(ip))
            {
                throw new LinkException(400, "url_khong_hop_le", "Khong rut gon dia chi mang noi bo.");
            }

            return new UrlChuan(uri.AbsoluteUri, tenMien);
        }

        /// <summary>tenMien co nam trong (hoac la ten mien con cua) mot ten mien bi chan.</summary>
        public static string? TenMienBiChan(string tenMien, IEnumerable<string> danhSachChan)
        {
            if (danhSachChan == null)
            {
                throw new ArgumentNullException(nameof(danhSachChan));
            }

            foreach (string chan in danhSachChan)
            {
                if (tenMien == chan || tenMien.EndsWith("." + chan, StringComparison.Ordinal))
                {
                    return chan;
                }
            }

            return null;
        }

        /// <summary>Cac ten mien cha cua tenMien (ke ca chinh no): a.b.x.com → a.b.x.com, b.x.com, x.com, com.</summary>
        public static List<string> CacTenMienCha(string tenMien)
        {
            List<string> ds = new List<string>();
            string s = tenMien;
            while (s.Length > 0)
            {
                ds.Add(s);
                int i = s.IndexOf('.', StringComparison.Ordinal);
                if (i < 0)
                {
                    break;
                }

                s = s.Substring(i + 1);
            }

            return ds;
        }

        public static string ChuanHoaTenMienChan(string? tenMien)
        {
            string s = tenMien == null ? string.Empty : tenMien.Trim().ToLowerInvariant().TrimEnd('.');
            if (s.StartsWith("*.", StringComparison.Ordinal))
            {
                s = s.Substring(2);
            }

            if (s.Length == 0 || s.Length > ShortLink.CotTenMien || s.Contains('/', StringComparison.Ordinal) || s.Contains(' ', StringComparison.Ordinal))
            {
                throw new LinkException(400, "ten_mien_khong_hop_le", "Ten mien khong hop le (vd: example.com).");
            }

            return s;
        }

        public static void KiemAlias(string alias)
        {
            if (!MauAlias.IsMatch(alias))
            {
                throw new LinkException(400, "alias_khong_hop_le", "Alias gom 3-32 ky tu chu, so, '-' hoac '_'.");
            }
        }

        public static string SinhMa(int doDai)
        {
            StringBuilder sb = new StringBuilder(doDai);
            for (int i = 0; i < doDai; i++)
            {
                sb.Append(BangChu[RandomNumberGenerator.GetInt32(BangChu.Length)]);
            }

            return sb.ToString();
        }

        private static bool LaDiaChiNoiBo(IPAddress ip)
        {
            if (IPAddress.IsLoopback(ip))
            {
                return true;
            }

            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal;
            }

            byte[] b = ip.GetAddressBytes();
            return b[0] == 10
                   || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                   || (b[0] == 192 && b[1] == 168)
                   || (b[0] == 169 && b[1] == 254)
                   || b[0] == 0;
        }
    }
}
