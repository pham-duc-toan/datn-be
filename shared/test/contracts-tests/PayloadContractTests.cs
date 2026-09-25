using System.Reflection;
using Crowd.BuildingBlocks.Messaging;
using Crowd.Contracts.Annotation;

namespace Crowd.Contracts.Tests;

/// <summary>
/// Canh gác toàn bộ <c>Crowd.Contracts</c>. Mỗi payload thêm vào đều tự động
/// nằm trong tầm kiểm — không phải nhớ viết test cho từng cái.
/// </summary>
public sealed class PayloadContractTests
{
    private static readonly Assembly Contracts = typeof(AnnotationApproved).Assembly;

    private static IReadOnlyList<Type> MoiPayload()
    {
        List<Type> ketQua = new List<Type>();

        foreach (Type kieu in Contracts.GetTypes())
        {
            bool laPayload = typeof(IEventPayload).IsAssignableFrom(kieu)
                             && !kieu.IsInterface
                             && !kieu.IsAbstract;

            if (laPayload)
            {
                ketQua.Add(kieu);
            }
        }

        ketQua.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
        return ketQua;
    }

    [Fact]
    public void Moi_payload_tuan_thu_quy_uoc()
    {
        // Bắt ba thứ cùng lúc:
        //   - thuộc tính không `required` và không nullable (thiếu trường => giá trị mặc định)
        //   - EventType sai định dạng
        //   - Version < 1
        var loi = EventPayloadContract.FindViolations(Contracts);

        Assert.True(loi.Count == 0, string.Join("\n", loi));
    }

    [Fact]
    public void Khong_co_hai_payload_dung_chung_mot_eventType()
    {
        // Trùng eventType nghĩa là consumer không biết phải đọc thành kiểu nào.
        var trung = MoiPayload()
            .GroupBy(t => (string)t.GetProperty(nameof(IEventPayload.EventType),
                BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(t => t.Name))}")
            .ToList();

        Assert.True(trung.Count == 0, string.Join("\n", trung));
    }

    [Fact]
    public void Moi_payload_deu_la_sealed_record()
    {
        // record => so sánh theo giá trị, cần cho round-trip test.
        // sealed => không ai kế thừa rồi thêm trường mà schema không biết.
        var sai = MoiPayload()
            .Where(t => !t.IsSealed
                        || t.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance) is null)
            .Select(t => t.Name)
            .ToList();

        Assert.True(sai.Count == 0,
            "Phải là `sealed record`: " + string.Join(", ", sai));
    }

    [Fact]
    public void Khong_payload_nao_dung_decimal_hoac_double_cho_tien()
    {
        // VND không có đơn vị lẻ. decimal/double mời gọi lỗi làm tròn (VD-M-07).
        var sai = MoiPayload()
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType == typeof(decimal) || p.PropertyType == typeof(double)
                            || p.PropertyType == typeof(decimal?) || p.PropertyType == typeof(double?))
                .Select(p => $"{t.Name}.{p.Name} ({p.PropertyType.Name})"))
            .ToList();

        Assert.True(sai.Count == 0,
            "Tiền phải là long (số nguyên đồng): " + string.Join(", ", sai));
    }

    [Fact]
    public void Co_it_nhat_mot_payload_de_cac_test_tren_khong_rong()
    {
        // Không có dòng này thì bốn test trên vẫn xanh khi Crowd.Contracts rỗng.
        Assert.NotEmpty(MoiPayload());
    }
}
