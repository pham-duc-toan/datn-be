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

        /// <summary>So bai dang lam do (bi huy neu huy du an).</summary>
        public int InProgressCount { get; init; }

        /// <summary>So bai DA NOP — con bai nop thi khong huy duoc (phai ket thuc som de cham).</summary>
        public int SubmittedCount { get; init; }
    }

    /// <summary>
    /// Hoi task-svc khi HUY du an da chay (hiem, khong o duong nong). Hoi CHU du lieu ngay tai cho
    /// vi ngay sau do ledger tra TOAN BO ky quy — luat muc 4 docs: quyet dinh tien bac khong doc ban
    /// sao. Moi loi (tat, het gio, ma loi) → DichVuKhongSanSangException (503): khong kiem duoc thi
    /// KHONG huy.
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
