using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Crowd.Labeling
{
    /// <summary>
    /// Hinh dang cua LabelPayload tren day (event, API):
    ///
    ///     { "taskType": "image", "schemaVersion": 1,
    ///       "data": { "loai_anh": { "labelIds": ["do"] }, "vat_the": [ {"labelId":"xe","x":10,"y":20,"w":100,"h":50} ] } }
    ///
    /// "data" la JSON long nhau that su, khong phai chuoi chua JSON — mo
    /// RabbitMQ UI la doc duoc ngay. Doc tu event KHONG kiem lai theo tap nhan
    /// (ben phat da kiem luc tao) — chi kiem cau truc va chuan hoa.
    ///
    /// Doc CHAT nhu ca envelope (UnmappedMemberHandling = Disallow): thieu
    /// truong hay co truong la deu nem loi.
    /// </summary>
    public sealed class LabelPayloadJsonConverter : JsonConverter<LabelPayload>
    {
        private const string TruongTaskType = "taskType";
        private const string TruongSchemaVersion = "schemaVersion";
        private const string TruongData = "data";

        public override LabelPayload Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("LabelPayload phai la mot object.");
            }

            string? taskType = null;
            int? schemaVersion = null;
            JsonElement? data = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("LabelPayload sai cau truc.");
                }

                string ten = reader.GetString() ?? string.Empty;
                reader.Read();

                if (ten == TruongTaskType)
                {
                    taskType = reader.GetString();
                }
                else if (ten == TruongSchemaVersion)
                {
                    schemaVersion = reader.GetInt32();
                }
                else if (ten == TruongData)
                {
                    using (JsonDocument doc = JsonDocument.ParseValue(ref reader))
                    {
                        data = doc.RootElement.Clone();
                    }
                }
                else
                {
                    throw new JsonException("LabelPayload khong co truong '" + ten + "'.");
                }
            }

            if (taskType == null || !schemaVersion.HasValue || !data.HasValue)
            {
                throw new JsonException("LabelPayload thieu taskType, schemaVersion hoac data.");
            }

            try
            {
                return LabelPayload.TuLuuTru(taskType, schemaVersion.Value, data.Value.GetRawText());
            }
            catch (LabelFormatException ex)
            {
                throw new JsonException(ex.Message, ex);
            }
        }

        public override void Write(Utf8JsonWriter writer, LabelPayload value, JsonSerializerOptions options)
        {
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            writer.WriteStartObject();
            writer.WriteString(TruongTaskType, value.TaskType);
            writer.WriteNumber(TruongSchemaVersion, value.SchemaVersion);
            writer.WritePropertyName(TruongData);

            using (JsonDocument doc = JsonDocument.Parse(value.DataJson))
            {
                doc.RootElement.WriteTo(writer);
            }

            writer.WriteEndObject();
        }
    }
}
