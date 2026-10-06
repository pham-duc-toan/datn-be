using System;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Crowd.Project.Infrastructure.Media
{
    /// <summary>Cau hinh doc thong tin media. Muc "Media" trong appsettings.</summary>
    public sealed class MediaOptions
    {
        public const string SectionName = "Media";

        /// <summary>Duong dan ffprobe. Mac dinh "ffprobe" (tim trong PATH).</summary>
        public string FfprobePath { get; set; } = "ffprobe";

        /// <summary>Thoi gian toi da cho mot lan ffprobe.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);
    }

    /// <summary>Thong tin doc duoc tu mot file audio / video.</summary>
    public sealed class ThongTinMedia
    {
        public double? DurationSec { get; init; }

        public int? Width { get; init; }

        public int? Height { get; init; }

        /// <summary>Ten dinh dang ffprobe nhan ra, vd "mov,mp4,m4a,3gp,3g2,mj2", "wav".</summary>
        public string? FormatName { get; init; }

        public bool CoVideo { get; init; }

        public bool CoAudio { get; init; }
    }

    public interface IMediaProbe
    {
        /// <summary>
        /// Doc thoi luong / kich thuoc cua file tai mot URL. null neu khong doc duoc
        /// (khong co ffprobe, file hong, khong phai media).
        /// </summary>
        Task<ThongTinMedia?> DocAsync(string url, CancellationToken ct);
    }

    /// <summary>
    /// Doc thong tin audio / video bang ffprobe (bo FFmpeg).
    ///
    /// Truyen cho ffprobe chinh LINK CO CHU KY cua MinIO: ffprobe doc qua HTTP,
    /// chi tai nhung phan can (dau file, chi muc moov...) — khong phai tai ca
    /// file video vai GB ve may.
    ///
    /// May khong co ffprobe thi tra null: manifest phai tu khai durationSec.
    /// </summary>
    public sealed class FfprobeMediaProbe : IMediaProbe
    {
        private readonly MediaOptions _options;
        private readonly ILogger<FfprobeMediaProbe> _logger;

        public FfprobeMediaProbe(IOptions<MediaOptions> options, ILogger<FfprobeMediaProbe> logger)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _options = options.Value;
            _logger = logger;
        }

        public async Task<ThongTinMedia?> DocAsync(string url, CancellationToken ct)
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = _options.FfprobePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            // ArgumentList tu boc tung tham so — URL co ky tu dac biet (&, ?) van an toan,
            // khong phai tu ghep chuoi lenh.
            psi.ArgumentList.Add("-v");
            psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-print_format");
            psi.ArgumentList.Add("json");
            psi.ArgumentList.Add("-show_format");
            psi.ArgumentList.Add("-show_streams");
            psi.ArgumentList.Add(url);

            Process? p;
            try
            {
                p = Process.Start(psi);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                _logger.LogWarning(ex, "Khong chay duoc ffprobe ({Path}) — can khai durationSec trong manifest", _options.FfprobePath);
                return null;
            }

            if (p == null)
            {
                return null;
            }

            using (p)
            using (CancellationTokenSource het = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                het.CancelAfter(_options.Timeout);
                Task<string> docRa = p.StandardOutput.ReadToEndAsync(het.Token);
                Task<string> docLoi = p.StandardError.ReadToEndAsync(het.Token);

                try
                {
                    await p.WaitForExitAsync(het.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    try
                    {
                        p.Kill(true);
                    }
                    catch (InvalidOperationException)
                    {
                        // Da tu thoat.
                    }

                    _logger.LogWarning("ffprobe qua {Timeout} — bo qua", _options.Timeout);
                    return null;
                }

                string json = await docRa.ConfigureAwait(false);
                string loi = await docLoi.ConfigureAwait(false);

                if (p.ExitCode != 0)
                {
                    _logger.LogInformation("ffprobe khong doc duoc file: {Loi}", loi.Trim());
                    return null;
                }

                return Phan(json);
            }
        }

        /// <summary>Lay duration o format (hoac stream), width/height o stream video dau tien.</summary>
        internal static ThongTinMedia? Phan(string json)
        {
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement goc = doc.RootElement;
                double? thoiLuong = null;
                string? dinhDang = null;

                JsonElement format;
                if (goc.TryGetProperty("format", out format))
                {
                    thoiLuong = SoThuc(format, "duration");
                    JsonElement ten;
                    if (format.TryGetProperty("format_name", out ten))
                    {
                        dinhDang = ten.GetString();
                    }
                }

                int? rong = null;
                int? cao = null;
                bool coVideo = false;
                bool coAudio = false;

                JsonElement streams;
                if (goc.TryGetProperty("streams", out streams))
                {
                    foreach (JsonElement s in streams.EnumerateArray())
                    {
                        JsonElement loai;
                        string? codecType = s.TryGetProperty("codec_type", out loai) ? loai.GetString() : null;

                        if (codecType == "audio")
                        {
                            coAudio = true;
                        }

                        // Anh bia nhung trong mp3 cung la "video" — bo qua stream attached_pic.
                        bool laAnhBia = false;
                        JsonElement disposition;
                        if (s.TryGetProperty("disposition", out disposition))
                        {
                            JsonElement pic;
                            laAnhBia = disposition.TryGetProperty("attached_pic", out pic) && pic.ValueKind == JsonValueKind.Number && pic.GetInt32() == 1;
                        }

                        if (codecType == "video" && !laAnhBia && !coVideo)
                        {
                            coVideo = true;
                            JsonElement w;
                            JsonElement h;
                            if (s.TryGetProperty("width", out w) && s.TryGetProperty("height", out h))
                            {
                                rong = w.GetInt32();
                                cao = h.GetInt32();
                            }
                        }

                        if (!thoiLuong.HasValue)
                        {
                            thoiLuong = SoThuc(s, "duration");
                        }
                    }
                }

                if (!thoiLuong.HasValue && !coVideo && !coAudio)
                {
                    return null;
                }

                return new ThongTinMedia
                {
                    DurationSec = thoiLuong,
                    Width = rong,
                    Height = cao,
                    FormatName = dinhDang,
                    CoVideo = coVideo,
                    CoAudio = coAudio,
                };
            }
        }

        private static double? SoThuc(JsonElement e, string ten)
        {
            JsonElement v;
            if (!e.TryGetProperty(ten, out v) || v.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            double kq;
            if (double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out kq) && kq > 0)
            {
                return kq;
            }

            return null;
        }
    }
}
