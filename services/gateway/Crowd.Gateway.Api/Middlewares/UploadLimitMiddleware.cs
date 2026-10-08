using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Crowd.Gateway.Api.Middlewares
{
    /// <summary>
    /// Nang tran kich thuoc body cho DUY NHAT endpoint upload dataset.
    ///
    /// Kestrel mac dinh chan body tren 30 MB — tot cho moi endpoint, tru upload
    /// ZIP anh (tran = setting dataset.zip_max_bytes, doc MOI request). Nang tran
    /// cho CA gateway thi ai cung gui duoc body lon vao bat ky endpoint nao — mo
    /// mot cua tu choi dich vu re tien. Nen chi nang dung duong dan can.
    ///
    /// Phai chay TRUOC Ocelot: tran chi doi duoc truoc khi body bat dau bi doc.
    /// </summary>
    public sealed class UploadLimitMiddleware
    {
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

        public Task InvokeAsync(HttpContext context, ISettings settings)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            bool laUpload = HttpMethods.IsPost(context.Request.Method)
                            && DuongDanUpload.IsMatch(context.Request.Path.Value ?? string.Empty);

            if (laUpload)
            {
                IHttpMaxRequestBodySizeFeature? tran = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (tran != null && !tran.IsReadOnly)
                {
                    tran.MaxRequestBodySize = settings.SoLon(SettingKeys.DatasetZipMaxBytes);
                }
            }

            return _next(context);
        }
    }
}
