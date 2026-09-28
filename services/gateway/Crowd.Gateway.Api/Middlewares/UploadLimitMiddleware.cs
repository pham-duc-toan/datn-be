using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Crowd.Gateway.Api.Middlewares
{
    /// <summary>
    /// Nang tran kich thuoc body cho DUY NHAT endpoint upload dataset.
    ///
    /// Kestrel mac dinh chan body tren 30 MB — tot cho moi endpoint, tru upload
    /// ZIP anh (toi 200 MB). Nang tran cho CA gateway thi ai cung gui duoc body
    /// 200 MB vao bat ky endpoint nao — mo mot cua tu choi dich vu re tien. Nen
    /// chi nang dung duong dan can.
    ///
    /// Phai chay TRUOC Ocelot: tran chi doi duoc truoc khi body bat dau bi doc.
    /// </summary>
    public sealed class UploadLimitMiddleware
    {
        public const long TranUpload = 200L * 1024 * 1024;

        private static readonly Regex DuongDanUpload = new Regex(
            "^/projects/[0-9a-fA-F-]{36}/datasets$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly RequestDelegate _next;

        public UploadLimitMiddleware(RequestDelegate next)
        {
            if (next == null)
            {
                throw new ArgumentNullException(nameof(next));
            }

            _next = next;
        }

        public Task InvokeAsync(HttpContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            bool laUpload = HttpMethods.IsPost(context.Request.Method)
                            && DuongDanUpload.IsMatch(context.Request.Path.Value ?? string.Empty);

            if (laUpload)
            {
                IHttpMaxRequestBodySizeFeature? tran = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (tran != null && !tran.IsReadOnly)
                {
                    tran.MaxRequestBodySize = TranUpload;
                }
            }

            return _next(context);
        }
    }
}
