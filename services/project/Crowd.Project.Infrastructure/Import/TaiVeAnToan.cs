using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Crowd.Project.Infrastructure.Import
{
    /// <summary>Cau hinh "DatasetImport".</summary>
    public sealed class NhapNgoaiOptions
    {
        public const string SectionName = "DatasetImport";

        /// <summary>
        /// Ten host noi bo DUOC phep tai (vd "minio" trong stack demo de thu nhap tu S3 tu dung). Mac dinh
        /// rong: moi dia chi noi bo bi chan.
        /// </summary>
        public string[] AllowedPrivateHosts { get; set; } = Array.Empty<string>();

        /// <summary>Khoa API Google (Drive API v3) — can de liet ke thu muc Drive cong khai. Rong = chi nhan link file.</summary>
        public string GoogleApiKey { get; set; } = string.Empty;
    }

    /// <summary>
    /// CHONG SSRF khi server tai file theo URL NGUOI DUNG nhap (FB-11): neu khong chan, doanh nghiep nhap
    /// "http://169.254.169.254/..." (metadata cloud), "http://identity:8101/internal/..." hay
    /// "http://localhost:5432" la bat server goi vao mang noi bo thay ho.
    ///
    /// Kiem o LUC KET NOI (ConnectCallback cua SocketsHttpHandler) tren DUNG dia chi IP se noi toi — khong
    /// kiem ten mien truoc roi moi noi (DNS rebinding: lan phan giai thu nhat tra IP cong khai, lan thu hai
    /// tra 127.0.0.1). Chuyen huong khong tu theo: tu xu ly toi da 3 lan, moi lan ket noi lai qua cung cua kiem.
    /// </summary>
    public static class ChanSsrf
    {
        /// <summary>Dia chi KHONG duoc goi: loopback, mang rieng, link-local (metadata cloud), CGNAT, multicast...</summary>
        public static bool LaDiaChiNoiBo(IPAddress ip)
        {
            if (ip == null)
            {
                throw new ArgumentNullException(nameof(ip));
            }

            if (ip.IsIPv4MappedToIPv6)
            {
                ip = ip.MapToIPv4();
            }

            if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.None))
            {
                return true;
            }

            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                byte[] b = ip.GetAddressBytes();
                return b[0] == 10
                    || b[0] == 0
                    || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                    || (b[0] == 192 && b[1] == 168)
                    || (b[0] == 169 && b[1] == 254)
                    || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                    || (b[0] == 192 && b[1] == 0 && b[2] == 0)
                    || (b[0] == 198 && (b[1] == 18 || b[1] == 19))
                    || b[0] >= 224;
            }

            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                byte[] b = ip.GetAddressBytes();
                bool ula = (b[0] & 0xFE) == 0xFC;
                return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast || ula;
            }

            return true;
        }

        /// <summary>
        /// Handler HTTP chi noi toi dia chi cong khai (tru host trong danh sach cho phep). Khong tu theo
        /// chuyen huong, khong dung proxy he thong.
        /// </summary>
        public static SocketsHttpHandler TaoHandler(IReadOnlyCollection<string> hostChoPhep)
        {
            HashSet<string> choPhep = new HashSet<string>(hostChoPhep ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            SocketsHttpHandler h = new SocketsHttpHandler();
            h.AllowAutoRedirect = false;
            h.UseProxy = false;
            h.ConnectTimeout = TimeSpan.FromSeconds(10);
            h.ConnectCallback = async (ctx, ct) =>
            {
                string host = ctx.DnsEndPoint.Host;
                IPAddress[] diaChi;
                IPAddress? mot;
                if (IPAddress.TryParse(host, out mot))
                {
                    diaChi = new IPAddress[] { mot };
                }
                else
                {
                    diaChi = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
                }

                if (diaChi.Length == 0)
                {
                    throw new HttpRequestException("Khong phan giai duoc " + host);
                }

                bool laNoiBo = diaChi.Any(LaDiaChiNoiBo);
                if (laNoiBo && !choPhep.Contains(host))
                {
                    throw new NguonNgoaiException("dia_chi_noi_bo", "Không tải từ địa chỉ nội bộ (" + host + ").");
                }

                Socket s = new Socket(SocketType.Stream, ProtocolType.Tcp);
                s.NoDelay = true;
                try
                {
                    // Noi DUNG mang dia chi da kiem — khong phan giai lai lan nua (chong rebinding).
                    await s.ConnectAsync(diaChi, ctx.DnsEndPoint.Port, ct).ConfigureAwait(false);
                    return new NetworkStream(s, true);
                }
                catch
                {
                    s.Dispose();
                    throw;
                }
            };
            return h;
        }
    }

    /// <summary>Loi nguon ngoai co ma (URL sai, qua lon, dia chi noi bo, het gio...) — ghi vao loi cua dong.</summary>
    public sealed class NguonNgoaiException : Exception
    {
        public NguonNgoaiException(string code, string message)
            : base(message)
        {
            Code = code;
        }

        public string Code { get; }
    }

    /// <summary>Mot file da tai ve thu muc tam.</summary>
    public sealed class TepTaiVe : IDisposable
    {
        public TepTaiVe(string duongDan, long kichThuoc, string? contentType, string ten)
        {
            DuongDan = duongDan;
            KichThuoc = kichThuoc;
            ContentType = contentType;
            Ten = ten;
        }

        public string DuongDan { get; }

        public long KichThuoc { get; }

        public string? ContentType { get; }

        public string Ten { get; }

        public void Dispose()
        {
            try
            {
                File.Delete(DuongDan);
            }
            catch (IOException)
            {
                // Tep tam con bi khoa: he dieu hanh tu don sau.
            }
        }
    }

    /// <summary>Tai MOT URL (http/https) ve tep tam, co gioi han kich thuoc + thoi gian, qua cua ChanSsrf.</summary>
    public sealed class TaiVeAnToan
    {
        private const int SoLanChuyenHuongToiDa = 3;

        private readonly HttpClient _http;

        public TaiVeAnToan(HttpClient http)
        {
            if (http == null)
            {
                throw new ArgumentNullException(nameof(http));
            }

            _http = http;
        }

        public async Task<TepTaiVe> TaiAsync(string url, long toiDaByte, TimeSpan thoiGian, IReadOnlyDictionary<string, string>? header, CancellationToken ct)
        {
            Uri? uri = KiemUrl(url);
            using (CancellationTokenSource gio = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                gio.CancelAfter(thoiGian);
                for (int lan = 0; lan <= SoLanChuyenHuongToiDa; lan++)
                {
                    using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, uri))
                    {
                        if (header != null)
                        {
                            foreach (KeyValuePair<string, string> kv in header)
                            {
                                req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                            }
                        }

                        HttpResponseMessage r;
                        try
                        {
                            r = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, gio.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                        {
                            throw new NguonNgoaiException("het_gio", "Tải quá " + (int)thoiGian.TotalSeconds + " giây.");
                        }
                        catch (HttpRequestException ex) when (ex.InnerException is NguonNgoaiException)
                        {
                            throw (NguonNgoaiException)ex.InnerException;
                        }
                        catch (HttpRequestException ex)
                        {
                            throw new NguonNgoaiException("khong_tai_duoc", "Không kết nối được: " + ex.Message);
                        }

                        using (r)
                        {
                            int ma = (int)r.StatusCode;
                            if (ma >= 300 && ma < 400 && r.Headers.Location != null)
                            {
                                uri = KiemUrl(new Uri(uri!, r.Headers.Location).ToString());
                                continue;
                            }

                            if (!r.IsSuccessStatusCode)
                            {
                                throw new NguonNgoaiException("khong_tai_duoc", "Nguồn trả HTTP " + ma + ".");
                            }

                            long? khaiBao = r.Content.Headers.ContentLength;
                            if (khaiBao.HasValue && khaiBao.Value > toiDaByte)
                            {
                                throw new NguonNgoaiException("file_qua_lon", "File " + khaiBao.Value + " byte, tối đa " + toiDaByte + ".");
                            }

                            string tam = Path.Combine(Path.GetTempPath(), "crowd-import-" + Guid.NewGuid().ToString("N"));
                            long daDoc = 0;
                            try
                            {
                                using (Stream vao = await r.Content.ReadAsStreamAsync(gio.Token).ConfigureAwait(false))
                                using (FileStream ra = new FileStream(tam, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                                {
                                    byte[] dem = new byte[81920];
                                    int n;
                                    while ((n = await vao.ReadAsync(dem.AsMemory(), gio.Token).ConfigureAwait(false)) > 0)
                                    {
                                        daDoc += n;
                                        if (daDoc > toiDaByte)
                                        {
                                            throw new NguonNgoaiException("file_qua_lon", "File vượt " + toiDaByte + " byte.");
                                        }

                                        await ra.WriteAsync(dem.AsMemory(0, n), gio.Token).ConfigureAwait(false);
                                    }
                                }
                            }
                            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                            {
                                XoaTam(tam);
                                throw new NguonNgoaiException("het_gio", "Tải quá " + (int)thoiGian.TotalSeconds + " giây.");
                            }
                            catch
                            {
                                XoaTam(tam);
                                throw;
                            }

                            MediaTypeHeaderValue? loai = r.Content.Headers.ContentType;
                            return new TepTaiVe(tam, daDoc, loai == null ? null : loai.MediaType, TenTuUrl(uri!, r));
                        }
                    }
                }

                throw new NguonNgoaiException("chuyen_huong_qua_nhieu", "Chuyển hướng quá " + SoLanChuyenHuongToiDa + " lần.");
            }
        }

        /// <summary>Chi http / https, co host. Khong hop le → NguonNgoaiException.</summary>
        public static Uri KiemUrl(string url)
        {
            Uri? u;
            if (string.IsNullOrWhiteSpace(url) || url.Length > 2000 || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out u)
                || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) || string.IsNullOrEmpty(u.Host))
            {
                throw new NguonNgoaiException("url_khong_hop_le", "URL phải là http / https đầy đủ.");
            }

            if (!string.IsNullOrEmpty(u.UserInfo))
            {
                throw new NguonNgoaiException("url_khong_hop_le", "URL không được chứa tên / mật khẩu.");
            }

            return u;
        }

        private static string TenTuUrl(Uri u, HttpResponseMessage r)
        {
            ContentDispositionHeaderValue? cd = r.Content.Headers.ContentDisposition;
            string? ten = cd == null ? null : (cd.FileNameStar ?? cd.FileName);
            if (string.IsNullOrWhiteSpace(ten))
            {
                ten = Uri.UnescapeDataString(Path.GetFileName(u.AbsolutePath));
            }

            ten = (ten ?? string.Empty).Trim('"', ' ');
            return ten.Length == 0 ? "file" : (ten.Length > 200 ? ten.Substring(0, 200) : ten);
        }

        private static void XoaTam(string tam)
        {
            try
            {
                File.Delete(tam);
            }
            catch (IOException)
            {
                // bo qua
            }
        }
    }
}
