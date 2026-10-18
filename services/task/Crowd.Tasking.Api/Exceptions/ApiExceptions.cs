using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Labeling;
using Crowd.Tasking.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crowd.Tasking.Api.Exceptions
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
    /// <summary>
    /// Nop bai co cau tra loi SAI DINH DANG (khung ngoai anh, thieu cong cu bat buoc...) → 400
    /// bai_chua_hop_le kem invalidAnswers: frontend nhay toi tung cau de sua. Bai VAN dang lam.
    /// </summary>
    public sealed class BaiChuaHopLeException : Exception
    {
        public BaiChuaHopLeException(IReadOnlyList<Crowd.Tasking.Api.Dtos.InvalidAnswer> cauSai)
            : base("Co " + (cauSai == null ? 0 : cauSai.Count) + " cau tra loi chua dung dinh dang — sua roi nop lai.")
        {
            CauSai = cauSai ?? new List<Crowd.Tasking.Api.Dtos.InvalidAnswer>();
        }

        public IReadOnlyList<Crowd.Tasking.Api.Dtos.InvalidAnswer> CauSai { get; }
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

            if (exception is InvalidValueException invalid)
            {
                status = StatusCodes.Status400BadRequest;
                code = invalid.Code;
            }
            else if (exception is LabelFormatException nhanSai)
            {
                // Nhan sai dinh dang (Crowd.Labeling) — du lieu vao sai.
                status = StatusCodes.Status400BadRequest;
                code = nhanSai.Code;
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
            else if (exception is BaiChuaHopLeException)
            {
                status = StatusCodes.Status400BadRequest;
                code = "bai_chua_hop_le";
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

            BaiChuaHopLeException? baiSai = exception as BaiChuaHopLeException;
            if (baiSai != null)
            {
                chiTiet.Extensions["invalidAnswers"] = baiSai.CauSai;
            }

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
