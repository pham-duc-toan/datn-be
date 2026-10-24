using System;
using Crowd.BuildingBlocks.Messaging;

namespace Crowd.Contracts.Media
{
    /// <summary>
    /// media-svc da chuan bi xong (hoac khong chuan bi duoc) MOT file video vua nap: ban chuan dung chung
    /// moi nguoi xem de watermark chi phai nhung khung dau + khung cuoi. project-svc ghi trang thai vao cac
    /// mau dung file nay — con video dang chuan bi / loi thi chua cho publish, admin chua duyet duoc.
    ///
    /// Theo FILE (StorageKey) chu khong theo mau: video dai cat thanh nhieu doan dung chung mot file.
    /// </summary>
    public sealed record VideoPrepared : IEventPayload
    {
        public static string EventType
        {
            get { return "video.prepared"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid ProjectId { get; init; }

        public required string StorageKey { get; init; }

        /// <summary>false = file hong (ffmpeg khong doc duoc) — doanh nghiep phai xoa / nap lai.</summary>
        public required bool Succeeded { get; init; }

        /// <summary>Ly do khi Succeeded = false. null khi thanh cong.</summary>
        public string? Error { get; init; }
    }
}
