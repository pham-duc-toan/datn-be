using System;
using System.Threading.Tasks;
using Crowd.Gateway.Api.Sessions;
using Microsoft.AspNetCore.Http;

namespace Crowd.Gateway.Api.Middlewares
{
    /// <summary>
    /// Chan access token da bi thu hoi (VD-S-11): user bi khoa, doi / dat lai mat khau, dang xuat moi
    /// thiet bi → token phat truoc moc thu hoi bi 401 phien_da_bi_thu_hoi NGAY, khong doi het 15 phut.
    /// Chay TRUOC Ocelot nen ap dung ca route khong kiem token o gateway (/me, /links...).
    /// </summary>
    public sealed class ThuHoiPhienMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ThuHoiPhienStore _store;

        public ThuHoiPhienMiddleware(RequestDelegate next, ThuHoiPhienStore store)
        {
            if (next == null)
            {
                throw new ArgumentNullException(nameof(next));
            }

            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            _next = next;
            _store = store;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            TokenChuaKiem? token = TokenChuaKiem.Doc(context.Request);
            if (token != null && _store.DaThuHoi(token.UserId, token.IssuedAt))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsync(LoiJson.Tao(
                    StatusCodes.Status401Unauthorized,
                    "phien_da_bi_thu_hoi",
                    "Phiên đăng nhập đã bị thu hồi (đổi mật khẩu, đăng xuất mọi thiết bị hoặc tài khoản bị khoá) — hãy đăng nhập lại."));
                return;
            }

            await _next(context);
        }
    }
}
