using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Admin.Api.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Crowd.Admin.Api.Exceptions
{
    /// <summary>AdminException → ProblemDetails co truong "code" (cung hinh dang voi cac service khac).</summary>
    public sealed class ApiExceptionHandler : IExceptionHandler
    {
        private readonly IProblemDetailsService _problemDetails;

        public ApiExceptionHandler(IProblemDetailsService problemDetails)
        {
            if (problemDetails == null)
            {
                throw new ArgumentNullException(nameof(problemDetails));
            }

            _problemDetails = problemDetails;
        }

        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            if (httpContext == null)
            {
                throw new ArgumentNullException(nameof(httpContext));
            }

            AdminException? ex = exception as AdminException;
            if (ex == null)
            {
                return false;
            }

            httpContext.Response.StatusCode = ex.Status;
            ProblemDetails chiTiet = new ProblemDetails();
            chiTiet.Status = ex.Status;
            chiTiet.Title = ex.Code;
            chiTiet.Detail = ex.Message;
            chiTiet.Extensions["code"] = ex.Code;

            return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = chiTiet,
                Exception = exception,
            });
        }
    }
}
