using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Crowd.BuildingBlocks.Messaging;

/// <summary>
/// Đánh dấu một kiểu là payload của event. Dùng làm ràng buộc generic cho
/// <see cref="EventEnvelope{TPayload}"/> nên không thể publish một object tùy tiện.
/// </summary>
[SuppressMessage("Design", "CA1040:Avoid empty interfaces",
    Justification = "Marker dùng làm ràng buộc generic — đây là ngoại lệ chuẩn của CA1040. " +
                    "Attribute không dùng làm ràng buộc generic được nên không thay thế được.")]
public interface IEventPayload;

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
