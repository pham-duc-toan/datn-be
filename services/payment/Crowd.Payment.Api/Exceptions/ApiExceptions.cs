using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Payment.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crowd.Payment.Api.Exceptions
{
    /// <summary>Khong tim thay HOAC khong phai cua ban — gop lam mot (404).</summary>
    public sealed class NotFoundException : Exception
    {
        public NotFoundException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Khong du dieu kien (403) kem MA ly do de frontend giai thich cho labeler:
    /// "chua_du_uy_tin", "bi_chan_khoi_du_an"...
    /// </summary>
    public sealed class ForbiddenException : Exception
    {
        public ForbiddenException(string code, string message)
            : base(message)
        {
            Code = code;
        }

        public string Code { get; }
    }

    /// <summary>
    /// Doi ngoai le da biet thanh ProblemDetails o MOT cho — giong project-svc.
    ///   InvalidValueException → 400 | ForbiddenException → 403 | NotFoundException → 404
    ///   RuleViolationException, DbUpdateConcurrencyException → 409
    /// </summary>
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

            if (exception is InvalidValueException invalid)
            {
                status = StatusCodes.Status400BadRequest;
                code = invalid.Code;
            }
            else if (exception is RuleViolationException rule)
            {
                status = StatusCodes.Status409Conflict;
                code = rule.Code;
            }
            else if (exception is ForbiddenException forbidden)
            {
                status = StatusCodes.Status403Forbidden;
                code = forbidden.Code;
            }
            else if (exception is NotFoundException)
            {
                status = StatusCodes.Status404NotFound;
                code = "khong_tim_thay";
            }
            else if (exception is DbUpdateConcurrencyException)
            {
                status = StatusCodes.Status409Conflict;
                code = "xung_dot_dong_thoi";
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
