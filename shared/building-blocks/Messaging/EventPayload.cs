using System.Reflection;
using System.Runtime.CompilerServices;

namespace Crowd.BuildingBlocks.Messaging;

/// <summary>
/// Mọi payload của event phải cài interface này.
/// <para>
/// Hai thành viên <c>static abstract</c> buộc <c>eventType</c> và <c>version</c>
/// sống ngay cạnh hình dạng dữ liệu, nên <b>không thể</b> gắn nhầm
/// <c>eventType</c> của event này cho payload của event khác — lỗi đó giờ là
/// lỗi biên dịch chứ không còn là chuỗi ma thuật truyền sai.
/// </para>
/// </summary>
public interface IEventPayload
{
    /// <summary>
    /// Dạng <c>&lt;aggregate&gt;.&lt;quá_khứ&gt;</c>. Phải khớp một dòng trong
    /// <c>contracts/events/CATALOG.md</c>.
    /// </summary>
    static abstract string EventType { get; }

    /// <summary>
    /// Phiên bản schema của payload này. Tăng khi có breaking change và phát
    /// song song hai bản cho tới khi consumer cuối cùng chuyển xong (VD-D-08).
    /// </summary>
    static abstract int Version { get; }
}

/// <summary>
/// Kiểm quy ước bắt buộc cho payload: <b>mọi thuộc tính phải hoặc là
/// <c>required</c>, hoặc là nullable</b>.
/// <para>
/// Vì sao: thuộc tính không required và không nullable sẽ được
/// <c>System.Text.Json</c> điền <b>giá trị mặc định</b> khi JSON thiếu trường đó.
/// Với <c>long AmountVnd</c> thì thiếu trường nghĩa là <b>0 đồng, không một lời
/// cảnh báo</b> — đúng lớp lỗi VD-M-05 và VD-M-07.
/// </para>
/// <para>
/// Cạm bẫy hay gặp nhất là positional record: <c>record X(long AmountVnd)</c>
/// sinh ra thuộc tính KHÔNG required. Phải viết dạng <c>required</c> tường minh.
/// </para>
/// </summary>
public static class EventPayloadContract
{
    /// <summary>
    /// Quét mọi kiểu <see cref="IEventPayload"/> trong assembly và trả về danh sách
    /// vi phạm. Mỗi service gọi hàm này trong một test để quy ước không bị trôi.
    /// </summary>
    public static IReadOnlyList<string> FindViolations(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return FindViolations(assembly.GetTypes());
    }

    /// <summary>Kiểm một tập kiểu cụ thể. Kiểu không phải payload sẽ bị bỏ qua.</summary>
    public static IReadOnlyList<string> FindViolations(params Type[] types)
    {
        ArgumentNullException.ThrowIfNull(types);

        var nullability = new NullabilityInfoContext();
        var violations = new List<string>();

        var payloadTypes = types
            .Where(t => typeof(IEventPayload).IsAssignableFrom(t)
                        && t is { IsInterface: false, IsAbstract: false });

        foreach (var type in payloadTypes)
        {
            KiemKhaiBao(type, violations);

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (IsSafe(prop, nullability))
                {
                    continue;
                }

                violations.Add(
                    $"{type.Name}.{prop.Name} ({prop.PropertyType.Name}) — không `required` và " +
                    "không nullable. JSON thiếu trường này sẽ thành giá trị mặc định thay vì lỗi.");
            }
        }

        return violations;
    }

    /// <summary>
    /// Mô tả hình dạng mọi payload trong assembly dưới dạng văn bản tất định.
    /// Dùng làm <b>ảnh chụp hợp đồng</b>: commit kết quả vào repo, rồi một test so
    /// chuỗi này với file đã commit. Lệch nghĩa là hình dạng payload đã đổi — mà
    /// với <c>UnmappedMemberHandling = Disallow</c> thì mọi thay đổi hình dạng đều
    /// là breaking change (VD-D-12). Người sửa buộc phải nhìn thấy điều đó ngay
    /// lúc chạy test, thay vì phát hiện khi message đã rơi vào DLQ trên production.
    /// </summary>
    public static string Describe(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var nullability = new NullabilityInfoContext();
        var lines = new List<string>();

        var payloadTypes = assembly.GetTypes()
            .Where(t => typeof(IEventPayload).IsAssignableFrom(t)
                        && t is { IsInterface: false, IsAbstract: false })
            .Select(t => (Type: t, EventType: DocEventType(t), Version: DocVersion(t)))
            .OrderBy(x => x.EventType, StringComparer.Ordinal)
            .ThenBy(x => x.Type.Name, StringComparer.Ordinal);

        foreach (var (type, eventType, version) in payloadTypes)
        {
            lines.Add($"{eventType} v{version}  ({type.Name})");

            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .OrderBy(p => p.Name, StringComparer.Ordinal);

            foreach (var prop in props)
            {
                var kind = prop.GetCustomAttribute<RequiredMemberAttribute>() is not null
                    ? "required"
                    : IsSafe(prop, nullability) ? "nullable" : "KHÔNG-AN-TOÀN";

                lines.Add($"    {prop.Name,-24} {TenKieu(prop, nullability),-20} {kind}");
            }

            lines.Add(string.Empty);
        }

        return string.Join("\n", lines).TrimEnd() + "\n";
    }

    private static string? DocEventType(Type t) =>
        (string?)t.GetProperty(nameof(IEventPayload.EventType),
            BindingFlags.Public | BindingFlags.Static)?.GetValue(null);

    private static int? DocVersion(Type t) =>
        (int?)t.GetProperty(nameof(IEventPayload.Version),
            BindingFlags.Public | BindingFlags.Static)?.GetValue(null);

    private static string TenKieu(PropertyInfo prop, NullabilityInfoContext nullability)
    {
        var underlying = Nullable.GetUnderlyingType(prop.PropertyType);
        if (underlying is not null)
        {
            return underlying.Name + "?";
        }

        var nullableRef = !prop.PropertyType.IsValueType
                          && nullability.Create(prop).ReadState == NullabilityState.Nullable;

        return prop.PropertyType.Name + (nullableRef ? "?" : string.Empty);
    }

    /// <summary>Kiểm chính hai giá trị <c>EventType</c> và <c>Version</c> mà payload khai ra.</summary>
    private static void KiemKhaiBao(Type type, List<string> violations)
    {
        var eventType = (string?)type.GetProperty(nameof(IEventPayload.EventType),
            BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        var version = (int?)type.GetProperty(nameof(IEventPayload.Version),
            BindingFlags.Public | BindingFlags.Static)?.GetValue(null);

        if (eventType is null || version is null)
        {
            violations.Add($"{type.Name} không khai được EventType/Version.");
            return;
        }

        if (EventRules.Violation(eventType, version.Value, "n/a") is { } loi)
        {
            violations.Add($"{type.Name} khai sai: {loi}");
        }
    }

    private static bool IsSafe(PropertyInfo prop, NullabilityInfoContext nullability)
    {
        // required => System.Text.Json ném lỗi nếu JSON thiếu trường
        if (prop.GetCustomAttribute<RequiredMemberAttribute>() is not null)
        {
            return true;
        }

        // Nullable<T> => null là giá trị hợp lệ, vắng mặt là có chủ đích
        if (Nullable.GetUnderlyingType(prop.PropertyType) is not null)
        {
            return true;
        }

        // Kiểu tham chiếu được khai nullable => vắng mặt là có chủ đích
        return !prop.PropertyType.IsValueType
               && nullability.Create(prop).ReadState == NullabilityState.Nullable;
    }
}
