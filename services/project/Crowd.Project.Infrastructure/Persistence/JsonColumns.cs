using System;
using System.Collections.Generic;
using System.Text.Json;
using Crowd.Project.Domain.Projects;

namespace Crowd.Project.Infrastructure.Persistence
{
    /// <summary>
    /// Doi LabelSchema / Guideline qua lai voi chuoi JSON de luu vao cot JSONB.
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

            LabelSchemaJson dto = new LabelSchemaJson();
            dto.Kind = "classification";
            dto.Classes = new List<string>(schema.Classes);
            dto.AllowMultiple = schema.AllowMultiple;
            return JsonSerializer.Serialize(dto, Options);
        }

        public static LabelSchema DocLabelSchema(string json)
        {
            LabelSchemaJson? dto = JsonSerializer.Deserialize<LabelSchemaJson>(json, Options);
            if (dto == null || dto.Classes == null)
            {
                throw new InvalidOperationException("Cot label_schema hong: " + json);
            }

            return LabelSchema.TaoPhanLoai(dto.Classes, dto.AllowMultiple);
        }

        public static string VietGuideline(Guideline? guideline)
        {
            if (guideline == null)
            {
                throw new ArgumentNullException(nameof(guideline));
            }

            GuidelineJson dto = new GuidelineJson();
            dto.Markdown = guideline.Markdown;
            dto.Examples = new List<GuidelineExampleJson>();

            foreach (GuidelineExample vd in guideline.Examples)
            {
                GuidelineExampleJson e = new GuidelineExampleJson();
                e.SampleId = vd.SampleId;
                e.Label = vd.Label;
                e.IsCorrect = vd.IsCorrect;
                e.Explanation = vd.Explanation;
                dto.Examples.Add(e);
            }

            return JsonSerializer.Serialize(dto, Options);
        }

        public static Guideline DocGuideline(string json)
        {
            GuidelineJson? dto = JsonSerializer.Deserialize<GuidelineJson>(json, Options);
            if (dto == null || dto.Markdown == null)
            {
                throw new InvalidOperationException("Cot guideline hong: " + json);
            }

            List<GuidelineExample> viDu = new List<GuidelineExample>();
            if (dto.Examples != null)
            {
                foreach (GuidelineExampleJson e in dto.Examples)
                {
                    viDu.Add(new GuidelineExample(e.SampleId, e.Label, e.IsCorrect, e.Explanation ?? string.Empty));
                }
            }

            return Guideline.Tao(dto.Markdown, viDu);
        }

        // ---- Hinh dang luu tru. Setter public vi System.Text.Json can. ----

        private sealed class LabelSchemaJson
        {
            /// <summary>Loai schema — de sau nay them bounding box, NER... ma khong vo du lieu cu.</summary>
            public string? Kind { get; set; }

            public List<string>? Classes { get; set; }

            public bool AllowMultiple { get; set; }
        }

        private sealed class GuidelineJson
        {
            public string? Markdown { get; set; }

            public List<GuidelineExampleJson>? Examples { get; set; }
        }

        private sealed class GuidelineExampleJson
        {
            public Guid? SampleId { get; set; }

            public string? Label { get; set; }

            public bool IsCorrect { get; set; }

            public string? Explanation { get; set; }
        }
    }
}
