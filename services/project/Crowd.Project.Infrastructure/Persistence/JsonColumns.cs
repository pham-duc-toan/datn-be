using System;
using System.Text.Json;
using Crowd.Labeling;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Crowd.Project.Infrastructure.Persistence
{
    /// <summary>
    /// Doi LabelSchema qua lai voi chuoi JSON de luu vao cot JSONB.
    ///
    /// Vi sao khong serialize thang lop domain: lop domain bat bien, constructor
    /// private, co kiem luat — System.Text.Json khong dung lai duoc. Nen di qua
    /// mot lop "hinh dang luu tru" (DTO) rieng, roi dung lai bang chinh factory
    /// cua domain: du lieu doc tu DB cung phai qua dung cua kiem luat do.
    ///
    /// Hinh dang JSON o day la HOP DONG VOI DATABASE: doi ten truong la du lieu
    /// cu doc khong ra. Them truong moi thi phai nullable.
    /// </summary>
    public static class JsonColumns
    {
        /// <summary>RawJson (khoi JSON bat bien) &lt;-&gt; cot jsonb.</summary>
        public static readonly ValueConverter<RawJson, string> RawJsonCot = new ValueConverter<RawJson, string>(
            v => v.Json,
            s => RawJson.Tu(s));

        /// <summary>
        /// Ban cho cot cho phep NULL. EF khong goi converter voi null (cot null thi
        /// EF tu ghi NULL), nen "v!" an toan.
        /// </summary>
        public static readonly ValueConverter<RawJson?, string> RawJsonCotNull = new ValueConverter<RawJson?, string>(
            v => v!.Json,
            s => RawJson.Tu(s));

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        /// <summary>
        /// Tham so nullable chi de khop kieu cot nullable; EF KHONG BAO GIO goi
        /// ham nay voi null (cot null thi EF tu ghi NULL, bo qua converter).
        /// </summary>
        public static string VietLabelSchema(LabelSchema? schema)
        {
            if (schema == null)
            {
                throw new ArgumentNullException(nameof(schema));
            }

            // Dang CHUAN cua Crowd.Labeling: cung khoi JSON nay duoc chep sang
            // task-svc / annotation-svc qua project.published.
            return schema.ToRawJson().Json;
        }

        public static LabelSchema DocLabelSchema(string json)
        {
            // Doc lai cung qua dung cua kiem (JSON Schema + ngu nghia) nhu luc tao.
            return LabelSchema.Doc(json);
        }
    }
}
