using Crowd.BuildingBlocks.Messaging;
using Crowd.Contracts.Annotation;

namespace Crowd.Contracts.Tests;

/// <summary>
/// Ảnh chụp hợp đồng payload — tầng cảnh báo <b>lúc build</b>.
/// <para>
/// Vì <c>UnmappedMemberHandling = Disallow</c> (VD-D-12), mọi thay đổi hình dạng
/// payload đều là breaking change với consumer chưa deploy lại. Test này bắt điều
/// đó ngay lúc gõ code, thay vì để phát hiện khi message đã rơi vào DLQ trên
/// production và labeler không được trả tiền.
/// </para>
/// </summary>
public sealed class PayloadSnapshotTests
{
    private const string BienMoiTruongCapNhat = "UPDATE_CONTRACT_SNAPSHOT";

    private static readonly string DuongDan =
        Path.Combine(TimGocRepo(), "contracts", "payloads.snapshot.txt");

    [Fact]
    public void Hinh_dang_payload_khong_doi_so_voi_anh_chup_da_commit()
    {
        var hienTai = EventPayloadContract.Describe(typeof(AnnotationApproved).Assembly);

        if (Environment.GetEnvironmentVariable(BienMoiTruongCapNhat) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DuongDan)!);
            File.WriteAllText(DuongDan, TieuDe + hienTai);
            return;
        }

        Assert.True(File.Exists(DuongDan),
            $"Chưa có ảnh chụp. Sinh lần đầu bằng:\n  {BienMoiTruongCapNhat}=1 dotnet test shared/test/contracts-tests");

        var daCommit = File.ReadAllText(DuongDan).Replace("\r\n", "\n", StringComparison.Ordinal);
        var moi = (TieuDe + hienTai).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.True(daCommit == moi, HuongDan(daCommit, moi));
    }

    private const string TieuDe =
        "# Ảnh chụp hợp đồng payload — SINH TỰ ĐỘNG, đừng sửa tay.\n" +
        "# Cập nhật: UPDATE_CONTRACT_SNAPSHOT=1 dotnet test shared/test/contracts-tests\n" +
        "# Mọi thay đổi trong file này là BREAKING CHANGE — xem VD-D-12.\n" +
        "\n";

    private static string HuongDan(string daCommit, string moi)
    {
        var cu = daCommit.Split('\n');
        var nay = moi.Split('\n');

        var them = nay.Except(cu, StringComparer.Ordinal).Where(l => l.Trim().Length > 0);
        var bot = cu.Except(nay, StringComparer.Ordinal).Where(l => l.Trim().Length > 0);

        return $"""

            ╔════════════════════════════════════════════════════════════════════╗
            ║  HỢP ĐỒNG PAYLOAD ĐÃ THAY ĐỔI — ĐÂY LÀ BREAKING CHANGE             ║
            ╚════════════════════════════════════════════════════════════════════╝

            Thêm / đổi:
            {string.Join("\n", them.Select(l => "  + " + l.Trim()))}

            Mất / đổi:
            {string.Join("\n", bot.Select(l => "  - " + l.Trim()))}

            Vì UnmappedMemberHandling = Disallow, consumer chưa deploy lại sẽ ném
            EventContractException và đẩy toàn bộ message vào DLQ (VD-D-12).

            CHỌN MỘT:

              1. Tăng Version của payload lên và phát song song hai bản cho tới khi
                 consumer cuối cùng chuyển xong (VD-D-08). An toàn nhất.

              2. Chấp nhận deploy ĐỒNG LOẠT mọi service consume event này, rồi cập
                 nhật ảnh chụp:
                     UPDATE_CONTRACT_SNAPSHOT=1 dotnet test shared/test/contracts-tests

              3. Hoàn tác thay đổi nếu nó ngoài ý muốn.

            """;
    }

    /// <summary>Đi ngược lên tới thư mục chứa <c>datn.slnx</c>.</summary>
    private static string TimGocRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "datn.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
               ?? throw new InvalidOperationException("Không tìm thấy gốc repo (datn.slnx).");
    }
}
