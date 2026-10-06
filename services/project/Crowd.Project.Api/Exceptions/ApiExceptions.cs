using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Labeling;
using Crowd.Project.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crowd.Project.Api.Exceptions
{
    /// <summary>
    /// Khong tim thay HOAC khong duoc xem — co y gop lam mot (404). Tra 403 cho
    /// du an rieng tu cua nguoi khac la tiet lo "du an nay co ton tai".
    /// </summary>
    public sealed class NotFoundException : Exception
    {
        public NotFoundException(string message)
            : base(message)
        {
        }
    }

    /// <summary>Xem duoc nhung khong duoc SUA (vd labeler goi API cua chu du an) → 403.</summary>
    public sealed class ForbiddenException : Exception
    {
        public ForbiddenException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Doi MOI ngoai le da biet thanh ProblemDetails voi ma HTTP dung, o MOT cho.
    /// Controller va service khong viet try/catch cho tung truong hop.
    ///
    ///   InvalidValueException          → 400  du lieu vao sai
    ///   LabelFormatException           → 400  nhan sai dinh dang (Crowd.Labeling)
    ///   ForbiddenException             → 403
    ///   NotFoundException              → 404
    ///   RuleViolationException         → 409  trang thai khong cho phep
    ///   DbUpdateConcurrencyException   → 409  nguoi khac vua sua cung luc (xmin, VD-D-11)
    ///   con lai                        → 500  (ASP.NET tu xu ly, khong lo chi tiet)
    ///
    /// Moi phan hoi loi co truong "code" on dinh de frontend dich (FC-08).
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

        public async ValueTask<bool> TryHandleAsync(
            HttpContext httpContext,
            Exception exception,
            CancellationToken cancellationToken)
        {
            int status;
            string code;
            string message;

            if (exception is InvalidValueException invalid)
            {
                status = StatusCodes.Status400BadRequest;
                code = invalid.Code;
                message = invalid.Message;
            }
            else if (exception is LabelFormatException nhanSai)
            {
                // Nhan / dap an sai dinh dang (Crowd.Labeling) — cung la du lieu vao sai.
                status = StatusCodes.Status400BadRequest;
                code = nhanSai.Code;
                message = nhanSai.Message;
            }
            else if (exception is RuleViolationException rule)
            {
                status = StatusCodes.Status409Conflict;
                code = rule.Code;
                message = rule.Message;
            }
            else if (exception is NotFoundException)
            {
                status = StatusCodes.Status404NotFound;
                code = "khong_tim_thay";
                message = exception.Message;
            }
            else if (exception is ForbiddenException)
            {
                status = StatusCodes.Status403Forbidden;
                code = "khong_co_quyen";
                message = exception.Message;
            }
            else if (exception is DbUpdateConcurrencyException)
            {
                status = StatusCodes.Status409Conflict;
                code = "xung_dot_dong_thoi";
                message = "Du lieu vua bi thay doi boi mot thao tac khac. Hay tai lai va thu lai.";
            }
            else
            {
                // Loi la: de ASP.NET tra 500 va ghi log day du.
                return false;
            }

            _logger.LogInformation("Tra {Status} {Code}: {Message}", status, code, message);

            httpContext.Response.StatusCode = status;

            ProblemDetails chiTiet = new ProblemDetails();
            chiTiet.Status = status;
            chiTiet.Title = code;
            chiTiet.Detail = message;
            chiTiet.Extensions["code"] = code;

            // HttpContext va Exception la "required init" — phai gan trong khoi { }.
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
