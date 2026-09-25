using System;
using Microsoft.AspNetCore.Http;

namespace Crowd.Identity.Api.Helpers
{
    /// <summary>
    /// Lay correlationId tu header do gateway gan vao. Chua co gateway thi tu
    /// sinh. Manh 4 (Correlation) se thay helper nay bang middleware dung chung.
    /// </summary>
    public static class CorrelationIdHelper
    {
        public const string TenHeader = "X-Correlation-Id";

        public static Guid LayTuRequest(HttpRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            string? tuHeader = request.Headers[TenHeader];

            Guid ketQua;
            if (tuHeader != null && Guid.TryParse(tuHeader, out ketQua))
            {
                return ketQua;
            }

            return Guid.CreateVersion7();
        }
    }
}
