using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Generic;

namespace Crowd.Admin.Api.Dtos
{
    public sealed class SettingResponse
    {
        public required string Key { get; init; }

        /// <summary>Nhom hien thi tren man hinh admin.</summary>
        public required string Group { get; init; }

        /// <summary>bool | int | long | double | durationSeconds | text.</summary>
        public required string Type { get; init; }

        public required string Unit { get; init; }

        public double? Min { get; init; }

        /// <summary>Voi text: do dai toi da.</summary>
        public double? Max { get; init; }

        /// <summary>newOperations (ap dung ngay cho thao tac moi) | restart (can khoi dong lai service).</summary>
        public required string Effect { get; init; }

        public required string Description { get; init; }

        /// <summary>Setting "chon mot": cac gia tri hop le (ve thanh danh sach chon). null = nhap tu do.</summary>
        public IReadOnlyList<string>? Choices { get; init; }

        public required JsonNode DefaultValue { get; init; }

        public JsonNode? Value { get; init; }

        public required long Version { get; init; }

        public DateTimeOffset? UpdatedAt { get; init; }

        public Guid? UpdatedBy { get; init; }
    }

    public sealed class UpdateSettingRequest
    {
        /// <summary>Gia tri moi: so, true/false hoac chuoi — dung kieu cua setting.</summary>
        public JsonElement? Value { get; init; }

        /// <summary>Ly do doi (ghi vao lich su).</summary>
        public string? Reason { get; init; }
    }

    public sealed class SettingHistoryResponse
    {
        public required long Version { get; init; }

        public JsonNode? OldValue { get; init; }

        public JsonNode? NewValue { get; init; }

        public Guid? ChangedBy { get; init; }

        public required DateTimeOffset ChangedAt { get; init; }

        public string? Reason { get; init; }
    }
}
