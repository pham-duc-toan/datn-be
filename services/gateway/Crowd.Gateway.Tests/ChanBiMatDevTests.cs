using System.Collections.Generic;
using Crowd.BuildingBlocks.Auth.Jwt;
using Xunit;

namespace Crowd.Gateway.Tests
{
    public sealed class ChanBiMatDevTests
    {
        [Fact]
        public void Phat_hien_mat_khau_va_khoa_dev()
        {
            List<KeyValuePair<string, string?>> cfg = new List<KeyValuePair<string, string?>>
            {
                new KeyValuePair<string, string?>("ConnectionStrings:IdentityDb", "Host=db;Password=dev_identity_pw"),
                new KeyValuePair<string, string?>("RabbitMq:Password", "a8f3c0d9e1b24c7f"),
                new KeyValuePair<string, string?>("InternalApi:Key", "demo_internal_key"),
                new KeyValuePair<string, string?>("MediaLinks:GrantKey", "pgZwtqPVxGvu3oA5esvFaUio+ltJ2sSfgOWqYQNHwyU="),
                new KeyValuePair<string, string?>("Rong", null),
            };
            Assert.Equal(new List<string> { "ConnectionStrings:IdentityDb", "InternalApi:Key", "MediaLinks:GrantKey" }, ChanBiMatDev.TimViPham(cfg));
        }

        [Fact]
        public void Bi_mat_that_khong_vi_pham()
        {
            List<KeyValuePair<string, string?>> cfg = new List<KeyValuePair<string, string?>>
            {
                new KeyValuePair<string, string?>("ConnectionStrings:IdentityDb", "Host=db;Password=9c1e0b6a77f2"),
                new KeyValuePair<string, string?>("Text", "developer_pw guide"),
            };
            Assert.Empty(ChanBiMatDev.TimViPham(cfg));
        }
    }
}
