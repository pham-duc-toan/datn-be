using System;
using System.Collections.Generic;

namespace Crowd.Notification.Api.Dtos
{
    public sealed class NotificationResponse
    {
        public required Guid Id { get; init; }

        public required string Type { get; init; }

        public required string Title { get; init; }

        public required string Body { get; init; }

        public string? Link { get; init; }

        public required DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset? ReadAt { get; init; }
    }

    public sealed class NotificationPageResponse
    {
        public required IReadOnlyList<NotificationResponse> Items { get; init; }

        public required int Page { get; init; }

        public required int PageSize { get; init; }

        public required int Total { get; init; }

        public required int UnreadCount { get; init; }
    }

    public sealed class UnreadCountResponse
    {
        public required int Count { get; init; }
    }

    public sealed class ReadAllResponse
    {
        public required int Updated { get; init; }
    }

    public sealed class PreferenceResponse
    {
        public required string Type { get; init; }

        public required string Label { get; init; }

        /// <summary>true = email bat buoc, khong tat duoc.</summary>
        public required bool Mandatory { get; init; }

        public required bool EmailEnabled { get; init; }
    }

    public sealed class UpdatePreferencesRequest
    {
        public IReadOnlyList<PreferenceItem>? Items { get; init; }
    }

    public sealed class PreferenceItem
    {
        public string? Type { get; init; }

        public bool EmailEnabled { get; init; }
    }
}
