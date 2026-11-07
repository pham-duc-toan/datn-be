using System;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Options;

namespace Crowd.BuildingBlocks.Auth.Internal
{
    /// <summary>
    /// gRPC NOI BO giua cac service: metadata x-internal-key phai khop khoa noi bo (InternalApi:Key) — cung
    /// khoa, cung phep so thoi gian co dinh voi [InternalApi] cua REST. Cong gRPC (HTTP/2, 91xx) khong qua
    /// gateway; khoa chan ca khi ai do cham thang cong trong mang noi bo.
    ///
    /// Ben phuc vu: services.AddGrpc(o => o.Interceptors.Add&lt;InternalKeyInterceptor&gt;()).
    /// Ben goi: truyen InternalGrpc.Khoa(options) lam headers cua moi loi goi.
    /// </summary>
    public sealed class InternalKeyInterceptor : Interceptor
    {
        private readonly IOptions<InternalApiOptions> _options;

        public InternalKeyInterceptor(IOptions<InternalApiOptions> options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            _options = options;
        }

        public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
            TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
        {
            KiemKhoa(context);
            return continuation(request, context);
        }

        public override Task ServerStreamingServerHandler<TRequest, TResponse>(
            TRequest request, IServerStreamWriter<TResponse> responseStream, ServerCallContext context,
            ServerStreamingServerMethod<TRequest, TResponse> continuation)
        {
            KiemKhoa(context);
            return continuation(request, responseStream, context);
        }

        private void KiemKhoa(ServerCallContext context)
        {
            string? guiLen = context.RequestHeaders.GetValue(InternalGrpc.Header);
            if (!InternalApiAttribute.KhoaKhop(_options.Value.Key, guiLen))
            {
                throw new RpcException(new Status(StatusCode.Unauthenticated, "Sai khoa noi bo."));
            }
        }
    }

    public static class InternalGrpc
    {
        public const string Header = "x-internal-key";

        /// <summary>Metadata mang khoa noi bo cho mot loi goi gRPC.</summary>
        public static Metadata Khoa(InternalApiOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            Metadata m = new Metadata();
            m.Add(Header, options.Key);
            return m;
        }

        /// <summary>
        /// Loi tam thoi (ben kia tat / qua tai / het gio) — noi goi tra 503 hoac thu lai vong sau, KHONG phai
        /// loi lap trinh.
        /// </summary>
        public static bool LaLoiTamThoi(RpcException ex)
        {
            if (ex == null)
            {
                throw new ArgumentNullException(nameof(ex));
            }

            return ex.StatusCode == StatusCode.Unavailable
                   || ex.StatusCode == StatusCode.DeadlineExceeded
                   || ex.StatusCode == StatusCode.ResourceExhausted
                   || ex.StatusCode == StatusCode.Cancelled;
        }
    }
}
