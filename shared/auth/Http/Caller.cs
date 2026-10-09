using System;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Correlation;
using Crowd.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Http;

namespace Crowd.BuildingBlocks.Auth.Http
{
    /// <summary>
    /// "Ai dang lam thao tac nay" — gom moi thu service can ve nguoi goi vao mot
    /// cho: danh tinh (tu TOKEN, VD-S-03), co phai admin khong, correlationId de
    /// gan vao event, va vai tro de ghi vao actor cua event (audit FP-06).
    ///
    /// Service nhan Caller thay vi HttpContext: service khong biet HTTP, va
    /// consumer/worker (khong co HTTP) cung tao duoc Caller "he thong".
    /// </summary>
    public sealed class Caller
    {
        private Caller(Guid? userId, bool isAdmin, ActorRole actorRole, Guid correlationId, Guid? causationId)
        {
            UserId = userId;
            IsAdmin = isAdmin;
            ActorRole = actorRole;
            CorrelationId = correlationId;
            CausationId = causationId;
        }

        /// <summary>null = he thong tu dong (consumer, worker).</summary>
        public Guid? UserId { get; }

        public bool IsAdmin { get; }

        /// <summary>Tu cach cua nguoi goi TRONG thao tac nay (mot tai khoan co the vua business vua labeler).</summary>
        public ActorRole ActorRole { get; }

        public Guid CorrelationId { get; }

        /// <summary>EventId da gay ra thao tac nay — chi co khi do consumer goi.</summary>
        public Guid? CausationId { get; }

        /// <summary>
        /// Tu request HTTP. vaiTro do CONTROLLER chon theo endpoint: tao du an la
        /// Business, tham gia la Labeler, duyet la Admin.
        /// </summary>
        public static Caller TuHttp(HttpContext http, ActorRole vaiTro)
        {
            if (http == null)
            {
                throw new ArgumentNullException(nameof(http));
            }

            Guid userId = CrowdClaims.GetUserId(http.User);
            bool laAdmin = http.User.IsInRole(CrowdRoles.Admin);

            return new Caller(userId, laAdmin, vaiTro, LayCorrelationId(http.Request), null);
        }

        /// <summary>
        /// Nguoi dung da xac thuc bang cach KHAC token (vd API key cua sharer, FS-05):
        /// service tu xac dinh userId roi tao Caller. Khong bao gio la admin.
        /// </summary>
        public static Caller TuNguoiDung(HttpContext http, Guid userId, ActorRole vaiTro)
        {
            if (http == null)
            {
                throw new ArgumentNullException(nameof(http));
            }

            return new Caller(userId, false, vaiTro, LayCorrelationId(http.Request), null);
        }

        /// <summary>He thong tu lam, do mot event gay ra (consumer) hoac do dong ho (worker).</summary>
        public static Caller HeThong(Guid correlationId, Guid? causationId)
        {
            return new Caller(null, true, ActorRole.System, correlationId, causationId);
        }

        /// <summary>Lay userId khi CHAC CHAN co nguoi dung — goi tu duong HTTP.</summary>
        public Guid LayUserId()
        {
            if (UserId == null)
            {
                throw new InvalidOperationException("Thao tac nay can nguoi dung, nhung dang chay voi tu cach he thong.");
            }

            return UserId.Value;
        }

        /// <summary>Actor ghi vao envelope. He thong thi null (quy uoc cua envelope).</summary>
        public EventActor? TaoActor()
        {
            if (UserId == null)
            {
                return null;
            }

            return new EventActor(UserId.Value, ActorRole);
        }

        /// <summary>
        /// Tin header correlation vi no do gateway gan (gateway luon ghi de ma
        /// client gui). Goi thang cong 8102 luc dev thi tu sinh.
        /// </summary>
        private static Guid LayCorrelationId(HttpRequest request)
        {
            string? tuHeader = request.Headers[CorrelationHeaders.HeaderName];

            Guid ketQua;
            if (tuHeader != null && Guid.TryParse(tuHeader, out ketQua))
            {
                return ketQua;
            }

            return Guid.CreateVersion7();
        }
    }
}
