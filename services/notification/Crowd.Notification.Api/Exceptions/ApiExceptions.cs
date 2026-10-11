using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Crowd.Notification.Api.Exceptions
{
    public sealed class InvalidValueException : Exception
    {
        public InvalidValueException(string code, string message)
            : base(message)
        {
            Code = code;
        }

        public string Code { get; }
    }

    /// <summary>Khong tim thay — hoac la thong bao cua NGUOI KHAC (BOLA: khong tiet lo no ton tai).</summary>
    public sealed class NotFoundException : Exception
    {
        public NotFoundException(string message)
            : base(message)
        {
        }
    }

    public sealed class ApiExceptionHandler : IExceptionHandler
    {
        private readonly IProblemDetailsService _problemDetails;
        private readonly ILogger<ApiExceptionHandler> _logger;

        public ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger)
        {
            if (problemDetails == null)
            {
                throw new ArgumentNullException(nameof(problemDetails));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _problemDetails = problemDetails;
            _logger = logger;
        }

        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            int status;
            string code;
            InvalidValueException? sai = exception as InvalidValueException;
            if (sai != null)
            {
                status = StatusCodes.Status400BadRequest;
                code = sai.Code;
            }
            else if (exception is NotFoundException)
            {
                status = StatusCodes.Status404NotFound;
                code = "khong_tim_thay";
            }
            else
            {
                return false;
            }

            _logger.LogInformation("Tra {Status} {Code}: {Message}", status, code, exception.Message);
            httpContext.Response.StatusCode = status;

            ProblemDetails chiTiet = new ProblemDetails();
            chiTiet.Status = status;
            chiTiet.Title = code;
            chiTiet.Detail = exception.Message;
            chiTiet.Extensions["code"] = code;

            ProblemDetailsContext ngucanh = new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = chiTiet,
                Exception = exception,
            };

            return await _problemDetails.TryWriteAsync(ngucanh);
        }
    }
}
