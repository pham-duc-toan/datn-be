using System.Net;
using Crowd.Project.Infrastructure.Import;

namespace Crowd.Project.Tests
{
    public sealed class NhapNguonNgoaiTests
    {
        [Theory]
        [InlineData("127.0.0.1", true)]
        [InlineData("10.88.0.5", true)]
        [InlineData("172.20.1.1", true)]
        [InlineData("192.168.1.10", true)]
        [InlineData("169.254.169.254", true)]
        [InlineData("100.64.0.1", true)]
        [InlineData("0.0.0.0", true)]
        [InlineData("::1", true)]
        [InlineData("fd00::1", true)]
        [InlineData("fe80::1", true)]
        [InlineData("::ffff:127.0.0.1", true)]
        [InlineData("8.8.8.8", false)]
        [InlineData("172.32.0.1", false)]
        [InlineData("2606:4700:4700::1111", false)]
        public void Nhan_dien_dia_chi_noi_bo(string ip, bool noiBo)
        {
            Assert.Equal(noiBo, ChanSsrf.LaDiaChiNoiBo(IPAddress.Parse(ip)));
        }

        [Theory]
        [InlineData("ftp://a.com/x.png")]
        [InlineData("file:///etc/passwd")]
        [InlineData("http://user:pass@a.com/x.png")]
        [InlineData("khong-phai-url")]
        public void Url_khong_hop_le(string url)
        {
            Assert.Equal("url_khong_hop_le", Assert.Throws<NguonNgoaiException>(() => TaiVeAnToan.KiemUrl(url)).Code);
        }

        [Theory]
        [InlineData("https://drive.google.com/file/d/1AbCdEfGhIjKlMnOp/view?usp=sharing", "1AbCdEfGhIjKlMnOp")]
        [InlineData("https://drive.google.com/open?id=1AbCdEfGhIjKlMnOp", "1AbCdEfGhIjKlMnOp")]
        [InlineData("https://drive.google.com/uc?export=download&id=1AbCdEfGhIjKlMnOp", "1AbCdEfGhIjKlMnOp")]
        [InlineData("https://example.com/file/d/1AbCdEfGhIjKlMnOp/view", null)]
        public void Ma_file_Drive(string link, string? ma)
        {
            Assert.Equal(ma, GoogleDrive.MaFile(link));
        }

        [Fact]
        public void Ma_thu_muc_Drive_va_link_tai()
        {
            Assert.Equal("1FolderIdXyz123", GoogleDrive.MaThuMuc("https://drive.google.com/drive/folders/1FolderIdXyz123?usp=sharing"));
            Assert.Equal("https://drive.google.com/uc?export=download&id=abc", GoogleDrive.LinkTai("abc", null));
            Assert.StartsWith("https://www.googleapis.com/drive/v3/files/abc?alt=media&key=", GoogleDrive.LinkTai("abc", "K"));
        }
    }
}
