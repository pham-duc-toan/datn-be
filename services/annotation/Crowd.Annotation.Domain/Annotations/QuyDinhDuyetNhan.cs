using System;

namespace Crowd.Annotation.Domain.Annotations
{
    /// <summary>
    /// Luat duyet / khieu nai DANG HIEU LUC — tang Api doc tu setting (admin-svc) roi
    /// truyen vao. Domain khong doc setting truc tiep.
    /// </summary>
    public sealed class QuyDinhDuyetNhan
    {
        /// <summary>annotation.reason_max_length (khong vuot do rong cot LabelAnnotation.CotDbLyDo).</summary>
        public required int DoDaiLyDoToiDa { get; init; }

        /// <summary>annotation.appeal_window.</summary>
        public required TimeSpan HanKhieuNai { get; init; }
    }
}
