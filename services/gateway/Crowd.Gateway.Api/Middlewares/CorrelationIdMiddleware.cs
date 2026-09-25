using System;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Correlation;
using Microsoft.AspNetCore.Http;

namespace Crowd.Gateway.Api.Middlewares
{
    /// <summary>
    /// Gan correlationId cho MOI request di vao he thong.
    ///
    /// LUON GHI DE ma client gui len, khong dung lai. Gateway la bien gioi voi
    /// Internet: tin ma cua client thi ke xau gui hang nghin request cung mot
    /// ma de lam roi log, hoac co tinh trung ma cua nguoi khac. Service NOI BO
    /// thi nguoc lai — tin header, vi header do chinh gateway gan.
    ///
    /// Phai dang ky TRUOC UseOcelot(): Ocelot la khau cuoi, request da bi
    /// chuyen di roi thi gan ma khong con tac dung.
    /// </summary>
    public sealed class CorrelationIdMiddleware
    {
        private readonly RequestDelegate _next;

        public CorrelationIdMiddleware(RequestDelegate next)
        {
            if (next == null)
            {
                throw new ArgumentNullException(nameof(next));
            }

            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            // Guid v7: 48 bit dau la thoi gian, nen sap xep theo ma la sap xep
            // theo thoi diem — xem log de hon Guid v4 ngau nhien.
            string correlationId = Guid.CreateVersion7().ToString();

            // Gan vao REQUEST: Ocelot copy header request sang service phia sau,
            // nen identity-svc nhan duoc dung ma nay.
            context.Request.Headers[CorrelationHeaders.HeaderName] = correlationId;

            // Gan vao RESPONSE: client (va ban, khi debug) biet request vua gui
            // mang ma nao de tra log.
            context.Response.Headers[CorrelationHeaders.HeaderName] = correlationId;

            await _next(context);
        }
    }
}
