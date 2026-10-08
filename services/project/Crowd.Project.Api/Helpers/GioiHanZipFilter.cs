using System;
using Crowd.BuildingBlocks.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Crowd.Project.Api.Helpers
{
    /// <summary>
    /// Ban DONG cua [RequestSizeLimit] + [RequestFormLimits]: tran body cua endpoint
    /// nap ZIP doc tu setting dataset.zip_max_bytes MOI request (attribute chi nhan
    /// hang so luc bien dich).
    ///
    /// Chay o pha resource filter — TRUOC model binding doc body — giong het cach hai
    /// attribute kia lam.
    /// </summary>
    public sealed class GioiHanZipFilter : IResourceFilter
    {
        private readonly ISettings _settings;

        public GioiHanZipFilter(ISettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            _settings = settings;
        }

        public void OnResourceExecuting(ResourceExecutingContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            long toiDa = _settings.SoLon(SettingKeys.DatasetZipMaxBytes);
            HttpContext http = context.HttpContext;

            IHttpMaxRequestBodySizeFeature? tranBody = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (tranBody != null && !tranBody.IsReadOnly)
            {
                tranBody.MaxRequestBodySize = toiDa;
            }

            // Form chua doc thi cai FormFeature voi tran moi (RequestFormLimits lam y nhu vay).
            IFormFeature? form = http.Features.Get<IFormFeature>();
            if (form == null || form.Form == null)
            {
                FormOptions tuyChon = new FormOptions();
                tuyChon.MultipartBodyLengthLimit = toiDa;
                http.Features.Set<IFormFeature>(new FormFeature(http.Request, tuyChon));
            }
        }

        public void OnResourceExecuted(ResourceExecutedContext context)
        {
        }
    }
}
