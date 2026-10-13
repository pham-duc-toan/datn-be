using System.Text.Json;
using System.Text.Json.Nodes;

namespace Crowd.Gateway.Api.Middlewares
{
    /// <summary>Than loi ProblemDetails co "code" — cung dang voi loi cua cac service.</summary>
    public static class LoiJson
    {
        public static string Tao(int status, string code, string detail)
        {
            JsonObject o = new JsonObject
            {
                ["status"] = status,
                ["title"] = code,
                ["detail"] = detail,
                ["code"] = code,
            };
            return o.ToJsonString(new JsonSerializerOptions());
        }
    }
}
