using System;
using System.Collections.Generic;
using Crowd.BuildingBlocks.Messaging;

namespace Crowd.Contracts.Quality
{
    /// <summary>
    /// quality-svc → identity-svc: admin xac nhan mot ho so nghi gian lan (FM-07). BlockAccounts = true
    /// thi identity khoa cac tai khoan trong UserIds (cung luong voi admin khoa tay: thu hoi phien, bao
    /// cho nguoi bi khoa). Phia Python dinh nghia lai DUNG cac truong nay (app/contracts.py).
    /// </summary>
    public sealed record FraudCaseConfirmed : IEventPayload
    {
        public static string EventType
        {
            get { return "fraud.case_confirmed"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid CaseId { get; init; }

        public required IReadOnlyList<Guid> UserIds { get; init; }

        public required string Reason { get; init; }

        public required bool BlockAccounts { get; init; }
    }
}
