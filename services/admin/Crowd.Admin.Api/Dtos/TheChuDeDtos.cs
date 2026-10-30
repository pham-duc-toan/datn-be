using System;

namespace Crowd.Admin.Api.Dtos
{
    /// <summary>POST /admin/tags — code bo trong thi tu sinh tu ten (bo dau, noi '-').</summary>
    public sealed class CreateTagRequest
    {
        /// <summary>domain | language.</summary>
        public string? Kind { get; init; }

        public string? Code { get; init; }

        public string? Name { get; init; }
    }

    /// <summary>PUT /admin/tags/{id} — chi doi ten hien thi (ma giu nguyen).</summary>
    public sealed class RenameTagRequest
    {
        public string? Name { get; init; }
    }

    /// <summary>POST /admin/tags/{id}/merge — gop the nay vao the dich (cung loai).</summary>
    public sealed class MergeTagRequest
    {
        public Guid TargetId { get; init; }
    }

    public sealed class TagResponse
    {
        public required Guid Id { get; init; }

        public required string Kind { get; init; }

        public required string Code { get; init; }

        public required string Name { get; init; }

        public required bool Hidden { get; init; }

        /// <summary>Ma the dich khi da gop; null = chua gop.</summary>
        public string? MergedInto { get; init; }

        public required DateTimeOffset UpdatedAt { get; init; }
    }
}
