using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Internal;
using Crowd.Project.Api.Exceptions;
using Microsoft.Extensions.Options;

namespace Crowd.Project.Api.Helpers
{
    /// <summary>Tra loi cua task-svc: GET /internal/projects/{id}/close-check.</summary>
    public sealed class TaskCloseCheck
    {
        public bool Paused { get; init; }

        public int ActiveLeases { get; init; }

        public int SubmittedCount { get; init; }
    }

    /// <summary>Tra loi cua annotation-svc: POST /internal/projects/{id}/close.</summary>
    public sealed class AnnotationCloseResult
    {
        public bool Closed { get; init; }

        public IReadOnlyList<string> Reasons { get; init; } = new List<string>();

        public int AnnotationCount { get; init; }

        public int SubmittedCount { get; init; }

        public int PendingReview { get; init; }

        public int ApprovedCount { get; init; }

        public int OpenAppeals { get; init; }

        public int RejectedInAppealWindow { get; init; }

        public DateTimeOffset? AppealWindowEndsAt { get; init; }
    }

    /// <summary>
    /// Goi task-svc va annotation-svc khi DONG du an (hiem, khong o duong nong). Hoi CHU du
    /// lieu ngay tai cho vi ngay sau do ledger tra ky quy — luat muc 4 docs: quyet dinh tien
    /// bac khong doc ban sao. Moi loi (tat, het gio, ma loi) → DichVuKhongSanSangException
    /// (503): khong kiem duoc thi KHONG dong.
    /// </summary>
    public sealed class DongSoClient
    {
        private readonly HttpClient _http;
        private readonly InternalApiOptions _options;

        public DongSoClient(HttpClient http, IOptions<InternalApiOptions> options)
        {
            if (http == null)
            {
                throw new ArgumentNullException(nameof(http));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            _http = http;
            _options = options.Value;
        }

        public async Task<TaskCloseCheck> KiemTaskAsync(Guid projectId, CancellationToken ct)
        {
            HttpRequestMessage req = new HttpRequestMessage(
                HttpMethod.Get, Noi(_options.TaskUrl, "internal/projects/" + projectId + "/close-check"));
            return await GuiAsync<TaskCloseCheck>(req, "task-svc", ct);
        }

        public async Task<AnnotationCloseResult> DongSoAnnotationAsync(Guid projectId, int soLuotNop, CancellationToken ct)
        {
            HttpRequestMessage req = new HttpRequestMessage(
                HttpMethod.Post, Noi(_options.AnnotationUrl, "internal/projects/" + projectId + "/close"));
            req.Content = JsonContent.Create(new { submittedCount = soLuotNop });
            return await GuiAsync<AnnotationCloseResult>(req, "annotation-svc", ct);
        }

        private async Task<T> GuiAsync<T>(HttpRequestMessage req, string ten, CancellationToken ct)
            where T : class
        {
            req.Headers.Add(InternalApiHeaders.Key, _options.Key);
            try
            {
                using (CancellationTokenSource hetGio = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    hetGio.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
                    HttpResponseMessage res = await _http.SendAsync(req, hetGio.Token);
                    if (!res.IsSuccessStatusCode)
                    {
                        throw new DichVuKhongSanSangException(
                            ten + " tra " + (int)res.StatusCode + " khi kiem dong du an. Thu lai sau.", null);
                    }

                    T? kq = await res.Content.ReadFromJsonAsync<T>(hetGio.Token);
                    if (kq == null)
                    {
                        throw new DichVuKhongSanSangException(ten + " tra ve rong khi kiem dong du an.", null);
                    }

                    return kq;
                }
            }
            catch (HttpRequestException ex)
            {
                throw new DichVuKhongSanSangException("Khong goi duoc " + ten + " de kiem dong du an. Thu lai sau.", ex);
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new DichVuKhongSanSangException(ten + " khong tra loi kip khi kiem dong du an. Thu lai sau.", ex);
            }
            finally
            {
                req.Dispose();
            }
        }

        private static Uri Noi(string goc, string duong)
        {
            if (string.IsNullOrWhiteSpace(goc))
            {
                throw new DichVuKhongSanSangException("Chua cau hinh InternalApi (dia chi service) cho project-svc.", null);
            }

            return new Uri(new Uri(goc.EndsWith('/') ? goc : goc + "/"), duong);
        }
    }
}
