using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Crowd.Identity.Api.Dtos
{
    // Ten truong o day do CHUAN OpenID Connect / JWK quy dinh (snake_case, viet
    // tat mot chu), khong phai do ta chon — nen phai ghi ro bang JsonPropertyName.

    /// <summary>Tai lieu discovery: noi service khac tim ra jwks_uri.</summary>
    public sealed class DiscoveryResponse
    {
        [JsonPropertyName("issuer")]
        public required string Issuer { get; init; }

        [JsonPropertyName("jwks_uri")]
        public required string JwksUri { get; init; }

        [JsonPropertyName("id_token_signing_alg_values_supported")]
        public required IReadOnlyList<string> SigningAlgValuesSupported { get; init; }
    }

    /// <summary>Danh sach khoa cong khai.</summary>
    public sealed class JwksResponse
    {
        [JsonPropertyName("keys")]
        public required IReadOnlyList<JwkResponse> Keys { get; init; }
    }

    /// <summary>
    /// Mot khoa RSA cong khai. CHI co n va e — khong co d, p, q (phan rieng).
    /// Lop nay khong co cho nao de chua phan rieng, nen khong the lo nham.
    /// </summary>
    public sealed class JwkResponse
    {
        [JsonPropertyName("kty")]
        public required string Kty { get; init; }

        [JsonPropertyName("use")]
        public required string Use { get; init; }

        [JsonPropertyName("alg")]
        public required string Alg { get; init; }

        [JsonPropertyName("kid")]
        public required string Kid { get; init; }

        [JsonPropertyName("n")]
        public required string N { get; init; }

        [JsonPropertyName("e")]
        public required string E { get; init; }
    }
}
