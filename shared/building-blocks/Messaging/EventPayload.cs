using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Crowd.BuildingBlocks.Messaging
{
    /// <summary>
    /// Mọi payload của event phải cài interface này.
    ///
    /// "static abstract" (C# 11) nghĩa là: kiểu nào cài interface này thì PHẢI
    /// có hai thuộc tính static cùng tên. Nhờ vậy EventEnvelope.Create đọc được
    /// TPayload.EventType ngay từ kiểu, không cần truyền chuỗi vào.
    ///
    /// Tác dụng: không thể gắn nhầm eventType của event này cho payload của
    /// event khác — lỗi đó giờ là lỗi biên dịch chứ không còn là chuỗi truyền sai.
    /// </summary>
    public interface IEventPayload
    {
        /// <summary>
        /// Dạng &lt;aggregate&gt;.&lt;quá_khứ&gt;.
        /// Phải khớp một dòng trong contracts/events/CATALOG.md.
        /// </summary>
        static abstract string EventType { get; }

        /// <summary>
        /// Phiên bản schema của payload này. Tăng khi có breaking change và phát
        /// song song hai bản cho tới khi consumer cuối cùng chuyển xong (VD-D-08).
        /// </summary>
        static abstract int Version { get; }
    }

    /// <summary>
    /// Kiểm quy ước bắt buộc cho payload: mọi thuộc tính phải hoặc là "required",
    /// hoặc là nullable.
    ///
    /// Vì sao: thuộc tính không required và không nullable sẽ được
    /// System.Text.Json điền GIÁ TRỊ MẶC ĐỊNH khi JSON thiếu trường đó. Với
    /// "long AmountVnd" thì thiếu trường nghĩa là 0 đồng, không một lời cảnh báo
    /// — đúng lớp lỗi VD-M-05 và VD-M-07.
    ///
    /// Cạm bẫy hay gặp nhất là positional record: "record X(long AmountVnd)"
    /// sinh ra thuộc tính KHÔNG required. Phải viết dạng required tường minh.
    /// </summary>
    public static class EventPayloadContract
    {
        /// <summary>
        /// Quét mọi kiểu IEventPayload trong assembly và trả về danh sách vi phạm.
        /// Mỗi service gọi hàm này trong một test để quy ước không bị trôi.
        /// </summary>
        public static IReadOnlyList<string> FindViolations(Assembly assembly)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            return FindViolations(assembly.GetTypes());
        }

        /// <summary>Kiểm một tập kiểu cụ thể. Kiểu không phải payload sẽ bị bỏ qua.</summary>
        public static IReadOnlyList<string> FindViolations(params Type[] types)
        {
            if (types == null)
            {
                throw new ArgumentNullException(nameof(types));
            }

            NullabilityInfoContext nullability = new NullabilityInfoContext();
            List<string> violations = new List<string>();

            foreach (Type type in LocPayload(types))
            {
                KiemKhaiBao(type, violations);

                PropertyInfo[] props = type.GetProperties(
                    BindingFlags.Public | BindingFlags.Instance);

                foreach (PropertyInfo prop in props)
                {
                    if (LaAnToan(prop, nullability))
                    {
                        continue;
                    }

                    violations.Add(
                        type.Name + "." + prop.Name + " (" + prop.PropertyType.Name + ") — "
                        + "không `required` và không nullable. JSON thiếu trường này sẽ "
                        + "thành giá trị mặc định thay vì lỗi.");
                }
            }

            return violations;
        }

        /// <summary>
        /// Mô tả hình dạng mọi payload trong assembly dưới dạng văn bản tất định.
        ///
        /// Dùng làm ẢNH CHỤP HỢP ĐỒNG: commit kết quả vào repo, rồi một test so
        /// chuỗi này với file đã commit. Lệch nghĩa là hình dạng payload đã đổi —
        /// mà với UnmappedMemberHandling = Disallow thì mọi thay đổi hình dạng
        /// đều là breaking change (VD-D-12). Người sửa buộc phải nhìn thấy điều
        /// đó ngay lúc chạy test, thay vì phát hiện khi message đã rơi vào DLQ.
        /// </summary>
        public static string Describe(Assembly assembly)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            NullabilityInfoContext nullability = new NullabilityInfoContext();
            StringBuilder ketQua = new StringBuilder();

            List<Type> payloads = LocPayload(assembly.GetTypes())
                .OrderBy(t => DocEventType(t), StringComparer.Ordinal)
                .ThenBy(t => t.Name, StringComparer.Ordinal)
                .ToList();

            foreach (Type type in payloads)
            {
                ketQua.Append(DocEventType(type))
                      .Append(" v")
                      .Append(DocVersion(type))
                      .Append("  (")
                      .Append(type.Name)
                      .Append(')')
                      .Append('\n');

                List<PropertyInfo> props = type
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .OrderBy(p => p.Name, StringComparer.Ordinal)
                    .ToList();

                foreach (PropertyInfo prop in props)
                {
                    string loai;
                    if (prop.GetCustomAttribute<RequiredMemberAttribute>() != null)
                    {
                        loai = "required";
                    }
                    else if (LaAnToan(prop, nullability))
                    {
                        loai = "nullable";
                    }
                    else
                    {
                        loai = "KHÔNG-AN-TOÀN";
                    }

                    // Căn cột: tên 24 ký tự, kiểu 20 ký tự, mỗi cột cách nhau
                    // một dấu cách. Định dạng phải ỔN ĐỊNH vì ảnh chụp hợp đồng
                    // so sánh từng ký tự — đổi khoảng trắng cũng làm test đỏ.
                    ketQua.Append("    ")
                          .Append(prop.Name.PadRight(24))
                          .Append(' ')
                          .Append(TenKieu(prop, nullability).PadRight(20))
                          .Append(' ')
                          .Append(loai)
                          .Append('\n');
                }

                ketQua.Append('\n');
            }

            return ketQua.ToString().TrimEnd('\n') + "\n";
        }

        private static List<Type> LocPayload(IEnumerable<Type> types)
        {
            List<Type> ketQua = new List<Type>();

            foreach (Type type in types)
            {
                bool laPayload = typeof(IEventPayload).IsAssignableFrom(type)
                                 && !type.IsInterface
                                 && !type.IsAbstract;

                if (laPayload)
                {
                    ketQua.Add(type);
                }
            }

            return ketQua;
        }

        /// <summary>Kiểm chính hai giá trị EventType và Version mà payload khai ra.</summary>
        private static void KiemKhaiBao(Type type, List<string> violations)
        {
            string? eventType = DocEventType(type);
            int? version = DocVersion(type);

            if (eventType == null || version == null)
            {
                violations.Add(type.Name + " không khai được EventType/Version.");
                return;
            }

            string? viPham = EventRules.Violation(eventType, version.Value, "n/a");
            if (viPham != null)
            {
                violations.Add(type.Name + " khai sai: " + viPham);
            }
        }

        private static string? DocEventType(Type type)
        {
            PropertyInfo? prop = type.GetProperty(
                nameof(IEventPayload.EventType), BindingFlags.Public | BindingFlags.Static);

            if (prop == null)
            {
                return null;
            }

            return (string?)prop.GetValue(null);
        }

        private static int? DocVersion(Type type)
        {
            PropertyInfo? prop = type.GetProperty(
                nameof(IEventPayload.Version), BindingFlags.Public | BindingFlags.Static);

            if (prop == null)
            {
                return null;
            }

            return (int?)prop.GetValue(null);
        }

        /// <summary>Thuộc tính này có an toàn khi JSON thiếu trường không.</summary>
        private static bool LaAnToan(PropertyInfo prop, NullabilityInfoContext nullability)
        {
            // required => System.Text.Json ném lỗi nếu JSON thiếu trường
            if (prop.GetCustomAttribute<RequiredMemberAttribute>() != null)
            {
                return true;
            }

            // Nullable<T> như Guid? => null là giá trị hợp lệ, vắng mặt là có chủ đích
            if (Nullable.GetUnderlyingType(prop.PropertyType) != null)
            {
                return true;
            }

            // Kiểu tham chiếu được khai nullable như string? => vắng mặt có chủ đích
            if (prop.PropertyType.IsValueType)
            {
                return false;
            }

            NullabilityInfo info = nullability.Create(prop);
            return info.ReadState == NullabilityState.Nullable;
        }

        private static string TenKieu(PropertyInfo prop, NullabilityInfoContext nullability)
        {
            Type? underlying = Nullable.GetUnderlyingType(prop.PropertyType);
            if (underlying != null)
            {
                return underlying.Name + "?";
            }

            if (prop.PropertyType.IsValueType)
            {
                return prop.PropertyType.Name;
            }

            NullabilityInfo info = nullability.Create(prop);
            if (info.ReadState == NullabilityState.Nullable)
            {
                return prop.PropertyType.Name + "?";
            }

            return prop.PropertyType.Name;
        }
    }
}
