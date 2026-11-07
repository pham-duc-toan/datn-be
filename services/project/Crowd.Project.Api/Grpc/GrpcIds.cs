using System;
using Grpc.Core;

namespace Crowd.Project.Api.Grpc
{
    internal static class GrpcIds
    {
        /// <summary>Id dang chuoi trong message gRPC → Guid; sai → INVALID_ARGUMENT.</summary>
        public static Guid Doc(string gt, string truong)
        {
            Guid id;
            if (!Guid.TryParse(gt, out id))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, truong + " khong phai uuid."));
            }

            return id;
        }
    }
}
