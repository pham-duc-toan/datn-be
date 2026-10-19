using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.BuildingBlocks.Settings;
using Crowd.BuildingBlocks.Storage;
using Crowd.Contracts.Identity;
using Crowd.Identity.Api.Dtos;
using Crowd.Identity.Api.Entities;
using Crowd.Identity.Api.Exceptions;
using Crowd.Identity.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Identity.Api.Services
{
    /// <summary>
    /// Xac minh doanh nghiep (FB-01 / FB-02 / FM-01). Doanh nghiep khai ho so + tai giay phep
    /// (PUT thang len bucket PRIVATE kyb-docs, link co han) → nop → admin duyet / tu choi.
    /// Chua xac minh thi project-svc khong cho publish du an (ban sao tu business.verified).
    /// </summary>
    public sealed class KybService
    {
        private static readonly HashSet<string> DuoiChoPhep = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".png", ".jpg", ".jpeg",
        };

        private readonly IdentityDbContext _db;
        private readonly IOutboxWriter _outbox;
        private readonly IObjectStorage _storage;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;

        public KybService(IdentityDbContext db, IOutboxWriter outbox, IObjectStorage storage, ISettings settings, TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (outbox == null)
            {
                throw new ArgumentNullException(nameof(outbox));
            }

            if (storage == null)
            {
                throw new ArgumentNullException(nameof(storage));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _db = db;
            _outbox = outbox;
            _storage = storage;
            _settings = settings;
            _clock = clock;
        }

        // =====================================================================
        // DOANH NGHIEP
        // =====================================================================

        public async Task<KybResponse> CuaToiAsync(Caller caller, CancellationToken ct)
        {
            Guid uid = caller.LayUserId();
            BusinessProfile? p = await _db.BusinessProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == uid, ct);
            return TaoResponse(p == null ? BusinessProfile.Tao(uid, _clock.GetUtcNow()) : p);
        }

        public async Task<KybResponse> CapNhatAsync(UpdateKybRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            BusinessProfile p = await LayHoacTaoAsync(caller.LayUserId(), ct);
            p.CapNhat(body.CompanyName, body.TaxCode, body.Address, _clock.GetUtcNow());

            await _db.SaveChangesAsync(ct);
            return TaoResponse(p);
        }

        public async Task<LicenseUploadResponse> XinLinkTaiGiayPhepAsync(LicenseUploadRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            string ten = Path.GetFileName((body.FileName ?? string.Empty).Trim());
            string duoi = Path.GetExtension(ten);
            if (ten.Length == 0 || ten.Length > 200 || !DuoiChoPhep.Contains(duoi))
            {
                throw new InvalidValueException("file_khong_hop_le", "Giay phep phai la file .pdf, .png hoac .jpg (ten toi da 200 ky tu).");
            }

            long toiDa = _settings.SoLon(SettingKeys.IdentityKybFileMaxBytes);
            if (body.SizeBytes <= 0 || body.SizeBytes > toiDa)
            {
                throw new InvalidValueException("file_qua_lon", "File giay phep toi da " + toiDa + " byte.");
            }

            Guid uid = caller.LayUserId();
            BusinessProfile p = await LayHoacTaoAsync(uid, ct);

            // Moi lan xin mot khoa moi: file cu (neu co) khong bi ghi de giua chung khi admin dang xem.
            string khoa = "kyb/" + uid + "/" + Guid.CreateVersion7() + duoi.ToLowerInvariant();
            p.GanGiayPhep(khoa, ten, _clock.GetUtcNow());
            await _db.SaveChangesAsync(ct);

            return new LicenseUploadResponse
            {
                UploadUrl = await _storage.TaoLinkTaiLenAsync(khoa),
                Method = "PUT",
                ExpiresAt = _clock.GetUtcNow() + _settings.ThoiGian(SettingKeys.UploadLinkTtl),
            };
        }

        public async Task<KybResponse> NopAsync(Caller caller, CancellationToken ct)
        {
            Guid uid = caller.LayUserId();
            BusinessProfile p = await LayHoacTaoAsync(uid, ct);

            // File phai THAT SU nam trong kho (client co the xin link roi khong tai) va dung kich thuoc.
            if (p.LicenseKey != null)
            {
                ThongTinFile? file = await _storage.ThongTinAsync(p.LicenseKey, ct);
                if (file == null)
                {
                    throw new RuleViolationException("thieu_giay_phep", "Chua tai file giay phep len (link da cap nhung chua co file).");
                }

                if (file.SizeBytes > _settings.SoLon(SettingKeys.IdentityKybFileMaxBytes))
                {
                    throw new InvalidValueException("file_qua_lon", "File giay phep vuot kich thuoc cho phep.");
                }
            }

            p.Nop(_clock.GetUtcNow());
            Phat(caller, new BusinessKybSubmitted { BusinessId = uid, CompanyName = p.CompanyName });

            await _db.SaveChangesAsync(ct);
            return TaoResponse(p);
        }

        // =====================================================================
        // ADMIN
        // =====================================================================

        public async Task<PagedResponse<KybAdminResponse>> HangDoiAsync(string? status, int page, int pageSize, CancellationToken ct)
        {
            int trang = page < 1 ? 1 : page;
            int co = pageSize < 1 || pageSize > 100 ? 20 : pageSize;

            KybStatus loc = KybStatus.Submitted;
            if (!string.IsNullOrWhiteSpace(status) && !Enum.TryParse(status, true, out loc))
            {
                throw new InvalidValueException("trang_thai_khong_hop_le", "status: draft | submitted | verified | rejected.");
            }

            IQueryable<BusinessProfile> q = _db.BusinessProfiles.AsNoTracking().Where(x => x.Status == loc);
            int tong = await q.CountAsync(ct);
            List<BusinessProfile> ds = await q.OrderByDescending(x => x.SubmittedAt).Skip((trang - 1) * co).Take(co).ToListAsync(ct);

            List<Guid> ids = ds.Select(x => x.UserId).ToList();
            Dictionary<Guid, User> users = await _db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);

            List<KybAdminResponse> items = new List<KybAdminResponse>();
            foreach (BusinessProfile p in ds)
            {
                User? u;
                if (users.TryGetValue(p.UserId, out u))
                {
                    items.Add(TaoAdminResponse(p, u, null));
                }
            }

            return new PagedResponse<KybAdminResponse> { Items = items, Page = trang, PageSize = co, Total = tong };
        }

        public async Task<KybAdminResponse> ChiTietAsync(Guid businessId, CancellationToken ct)
        {
            return await ChiTietAsync(businessId, null, ct);
        }

        /// <summary>caller != null: ghi audit lan xem giay to (S-07) — co cap link xem thi phat sensitive_document.viewed.</summary>
        public async Task<KybAdminResponse> ChiTietAsync(Guid businessId, Caller? caller, CancellationToken ct)
        {
            BusinessProfile? p = await _db.BusinessProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == businessId, ct);
            User? u = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == businessId, ct);
            if (p == null || u == null)
            {
                throw new NotFoundException("Khong tim thay ho so doanh nghiep.");
            }

            string? link = p.LicenseKey == null ? null : await _storage.TaoLinkXemAsync(p.LicenseKey);
            if (link != null && caller != null)
            {
                Phat(caller, new SensitiveDocumentViewed { SubjectUserId = businessId, DocumentKind = "kyb_license", LinkCount = 1 });
                await _db.SaveChangesAsync(ct);
            }

            return TaoAdminResponse(p, u, link);
        }

        public async Task<KybResponse> DuyetAsync(Guid businessId, Caller caller, CancellationToken ct)
        {
            BusinessProfile p = await LayCoSanAsync(businessId, ct);
            DateTimeOffset bayGio = _clock.GetUtcNow();
            p.Duyet(caller.LayUserId(), bayGio);
            Phat(caller, new BusinessVerified { BusinessId = businessId, VerifiedAt = bayGio });

            await _db.SaveChangesAsync(ct);
            return TaoResponse(p);
        }

        public async Task<KybResponse> TuChoiAsync(Guid businessId, string? lyDo, Caller caller, CancellationToken ct)
        {
            BusinessProfile p = await LayCoSanAsync(businessId, ct);
            p.TuChoi(caller.LayUserId(), lyDo, _clock.GetUtcNow());
            Phat(caller, new BusinessKybRejected { BusinessId = businessId, Reason = p.RejectReason! });

            await _db.SaveChangesAsync(ct);
            return TaoResponse(p);
        }

        // =====================================================================

        private async Task<BusinessProfile> LayHoacTaoAsync(Guid uid, CancellationToken ct)
        {
            BusinessProfile? p = await _db.BusinessProfiles.FirstOrDefaultAsync(x => x.UserId == uid, ct);
            if (p == null)
            {
                p = BusinessProfile.Tao(uid, _clock.GetUtcNow());
                _db.BusinessProfiles.Add(p);
            }

            return p;
        }

        private async Task<BusinessProfile> LayCoSanAsync(Guid uid, CancellationToken ct)
        {
            BusinessProfile? p = await _db.BusinessProfiles.FirstOrDefaultAsync(x => x.UserId == uid, ct);
            if (p == null)
            {
                throw new NotFoundException("Khong tim thay ho so doanh nghiep.");
            }

            return p;
        }

        private void Phat<TPayload>(Caller caller, TPayload payload)
            where TPayload : class, IEventPayload
        {
            _outbox.Enqueue(EventEnvelope.Create(
                producer: "identity-svc",
                correlationId: caller.CorrelationId,
                payload: payload,
                causationId: caller.CausationId,
                actor: caller.TaoActor()));
        }

        internal static KybResponse TaoResponse(BusinessProfile p)
        {
            return new KybResponse
            {
                BusinessId = p.UserId,
                Status = TenTrangThai(p.Status),
                CompanyName = p.CompanyName,
                TaxCode = p.TaxCode,
                Address = p.Address,
                HasLicense = p.LicenseKey != null,
                LicenseFileName = p.LicenseFileName,
                RejectReason = p.RejectReason,
                SubmittedAt = p.SubmittedAt,
                ReviewedAt = p.ReviewedAt,
            };
        }

        internal static string TenTrangThai(KybStatus s)
        {
            string ten = s.ToString();
            return char.ToLowerInvariant(ten[0]) + ten.Substring(1);
        }

        private static KybAdminResponse TaoAdminResponse(BusinessProfile p, User u, string? link)
        {
            return new KybAdminResponse
            {
                Profile = TaoResponse(p),
                Email = u.Email,
                DisplayName = u.DisplayName,
                AccountStatus = u.Status == UserStatus.Blocked ? "blocked" : "active",
                LicenseUrl = link,
            };
        }
    }
}
