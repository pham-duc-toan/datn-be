using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Crowd.Link.Api.Exceptions
{
    /// <summary>Loi nghiep vu cua link-svc: mang san ma HTTP va code cho frontend.</summary>
    public sealed class LinkException : Exception
    {
        public LinkException(int status, string code, string message)
            : base(message)
        {
            Status = status;
            Code = code;
        }

        public int Status { get; }

        public string Code { get; }
    }

    /// <summary>LinkException → ProblemDetails co truong "code" (cung hinh dang voi cac service khac).</summary>
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
            LinkException? ex = exception as LinkException;
            if (ex != null)
            {
                status = ex.Status;
                code = ex.Code;
            }
            else if ((exception as Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException) != null)
            {
                status = 409;
                code = "xung_dot_dong_thoi";
            }
            else
            {
                return false;
            }

            httpContext.Response.StatusCode = status;
            ProblemDetails chiTiet = new ProblemDetails();
            chiTiet.Status = status;
            chiTiet.Title = code;
            chiTiet.Detail = ex != null ? ex.Message : "Du lieu vua bi thay doi boi thao tac khac. Tai lai roi thu lai.";
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
