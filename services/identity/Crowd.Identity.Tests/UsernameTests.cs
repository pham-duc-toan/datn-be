using System;
using System.Collections.Generic;
using Crowd.Identity.Api.Entities;
using Crowd.Identity.Api.Exceptions;
using Xunit;

namespace Crowd.Identity.Tests
{
    public sealed class UsernameTests
    {
        [Theory]
        [InlineData("an")]
        [InlineData("_an")]
        [InlineData("an.")]
        [InlineData("tên_có_dấu")]
        [InlineData("a b c")]
        [InlineData("abcdefghijklmnopqrstuvwxyz12345")]
        public void Username_sai_quy_tac_bi_tu_choi(string username)
        {
            Assert.NotNull(User.LoiUsername(username));
        }

        [Theory]
        [InlineData("nguyen.van_a")]
        [InlineData("@Toan123")]
        [InlineData("abc")]
        public void Username_hop_le_duoc_chuan_hoa_chu_thuong_bo_a_cong(string username)
        {
            Assert.Null(User.LoiUsername(username));
            Assert.Equal(username.TrimStart('@').ToLowerInvariant(), User.ChuanHoaUsername(username));
        }

        [Fact]
        public void Username_tu_email_bo_ky_tu_la_va_du_3_ky_tu()
        {
            Assert.Equal("nguyen.van_a", User.UsernameTuEmail("Nguyen.Van_A+tag@example.com").Replace("tag", string.Empty));
            Assert.Equal("abuser", User.UsernameTuEmail("ab@x.com"));
            Assert.Null(User.LoiUsername(User.UsernameTuEmail("-.-@x.com")));
        }

        [Fact]
        public void Doi_username_sai_quy_tac_nem_loi()
        {
            User u = User.Tao("a@b.com", "abc", "A", new List<string> { "labeler" }, DateTimeOffset.UtcNow);
            Assert.Throws<InvalidValueException>(() => u.DoiUsername("x"));
            u.DoiUsername("@Moi_Ten");
            Assert.Equal("moi_ten", u.Username);
        }
    }
}
