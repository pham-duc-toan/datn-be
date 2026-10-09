using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Labeling;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Crowd.Gate.Api.Helpers
{
    /// <summary>Loi nghiep vu cua gate-svc: mang san ma HTTP va code cho frontend.</summary>
    public sealed class GateException : Exception
    {
        public GateException(int status, string code, string message)
            : base(message)
        {
            Status = status;
            Code = code;
        }

        public int Status { get; }

        public string Code { get; }
    }

    /// <summary>GateException / LabelFormatException → ProblemDetails co truong "code".</summary>
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

            int status;
            string code;
            GateException? g = exception as GateException;
            LabelFormatException? nhan = exception as LabelFormatException;
            if (g != null)
            {
                status = g.Status;
                code = g.Code;
            }
            else if (nhan != null)
            {
                status = 400;
                code = nhan.Code;
            }
            else
            {
                return false;
            }

            httpContext.Response.StatusCode = status;
            ProblemDetails chiTiet = new ProblemDetails();
            chiTiet.Status = status;
            chiTiet.Title = code;
            chiTiet.Detail = exception.Message;
            chiTiet.Extensions["code"] = code;

            return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = chiTiet,
                Exception = exception,
            });
        }
    }
}
