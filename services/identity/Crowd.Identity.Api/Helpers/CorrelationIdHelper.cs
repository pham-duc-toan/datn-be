using System;
using Crowd.BuildingBlocks.Correlation;
using Microsoft.AspNetCore.Http;

namespace Crowd.Identity.Api.Helpers
{
    /// <summary>
    /// Lay correlationId tu header do gateway gan vao.
    ///
    /// Service NOI BO tin header nay vi no do gateway gan (gateway luon ghi de
    /// ma client gui len). Khong co header — goi thang cong 8101 luc dev, khong
    /// qua gateway — thi tu sinh de van chay duoc.
    /// </summary>
    public static class CorrelationIdHelper
    {
        public static Guid LayTuRequest(HttpRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            string? tuHeader = request.Headers[CorrelationHeaders.HeaderName];

            Guid ketQua;
            if (tuHeader != null && Guid.TryParse(tuHeader, out ketQua))
            {
                return ketQua;
            }

            return Guid.CreateVersion7();
        }
    }
}
