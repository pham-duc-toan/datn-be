using System;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Correlation;
using Crowd.Gateway.Api.Middlewares;
using Microsoft.AspNetCore.Http;

namespace Crowd.Gateway.Tests
{
    /// <summary>
    /// Test middleware ma khong can dung server: DefaultHttpContext la mot
    /// HttpContext gia, va "khau tiep theo" (_next) la mot ham ghi lai header
    /// no nhin thay — dong vai Ocelot.
    /// </summary>
    public sealed class CorrelationIdMiddlewareTests
    {
        [Fact]
        public async Task Sinh_ma_Guid_va_gan_cung_mot_ma_vao_request_lan_response()
        {
            string? maKhauSauThay = null;
            RequestDelegate khauSau = context =>
            {
                maKhauSauThay = context.Request.Headers[CorrelationHeaders.HeaderName];
                return Task.CompletedTask;
            };

            CorrelationIdMiddleware middleware = new CorrelationIdMiddleware(khauSau);
            DefaultHttpContext http = new DefaultHttpContext();

            await middleware.InvokeAsync(http);

            Guid khongDung;
            Assert.True(Guid.TryParse(maKhauSauThay, out khongDung), "Ma phai la Guid: " + maKhauSauThay);
            Assert.Equal(maKhauSauThay, http.Response.Headers[CorrelationHeaders.HeaderName].ToString());
        }

        [Fact]
        public async Task Ghi_de_ma_client_tu_gui_len()
        {
            const string maCuaClient = "11111111-1111-1111-1111-111111111111";

            string? maKhauSauThay = null;
            RequestDelegate khauSau = context =>
            {
                maKhauSauThay = context.Request.Headers[CorrelationHeaders.HeaderName];
                return Task.CompletedTask;
            };

            CorrelationIdMiddleware middleware = new CorrelationIdMiddleware(khauSau);
            DefaultHttpContext http = new DefaultHttpContext();
            http.Request.Headers[CorrelationHeaders.HeaderName] = maCuaClient;

            await middleware.InvokeAsync(http);

            Assert.NotEqual(maCuaClient, maKhauSauThay);
        }

        [Fact]
        public async Task Hai_request_nhan_hai_ma_khac_nhau()
        {
            RequestDelegate khauSau = context => Task.CompletedTask;
            CorrelationIdMiddleware middleware = new CorrelationIdMiddleware(khauSau);

            DefaultHttpContext thuNhat = new DefaultHttpContext();
            DefaultHttpContext thuHai = new DefaultHttpContext();

            await middleware.InvokeAsync(thuNhat);
            await middleware.InvokeAsync(thuHai);

            Assert.NotEqual(
                thuNhat.Response.Headers[CorrelationHeaders.HeaderName].ToString(),
                thuHai.Response.Headers[CorrelationHeaders.HeaderName].ToString());
        }
    }
}
