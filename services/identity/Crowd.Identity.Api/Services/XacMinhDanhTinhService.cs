using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence;
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
    /// Xac minh danh tinh ca nhan: nguoi dung khai ho ten / ngay sinh / so giay to, tai anh hai mat
    /// giay to (PUT thang len bucket PRIVATE kyb-docs), nop → admin doi chieu → duyet / tu choi.
    /// Ket qua di theo claim identity_verified / adult cua access token (lam moi token sau khi duyet).
    /// Cung khuon voi KYB (KybService) de admin dung chung mot thoi quen duyet.
    /// </summary>
    public sealed class XacMinhDanhTinhService
    {
        private static readonly TimeSpan LechVietNam = TimeSpan.FromHours(7);

        private static readonly HashSet<string> DuoiChoPhep = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg",
        };

        private readonly IdentityDbContext _db;
        private readonly IOutboxWriter _outbox;
        private readonly IObjectStorage _storage;
        private readonly KhoaDuLieu _khoa;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;

        public XacMinhDanhTinhService(IdentityDbContext db, IOutboxWriter outbox, IObjectStorage storage, KhoaDuLieu khoa, ISettings settings, TimeProvider clock)
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

            if (khoa == null)
            {
                throw new ArgumentNullException(nameof(khoa));
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
            _khoa = khoa;
            _settings = settings;
            _clock = clock;
        }

        public static DateOnly HomNayVietNam(DateTimeOffset luc)
        {
            return DateOnly.FromDateTime(luc.ToOffset(LechVietNam).DateTime);
        }

        // =====================================================================
        // NGUOI DUNG
        // =====================================================================

        public async Task<IdentityVerificationResponse> CuaToiAsync(Guid userId, CancellationToken ct)
        {
            XacMinhDanhTinh? x = await _db.XacMinhDanhTinhs.AsNoTracking().FirstOrDefaultAsync(v => v.UserId == userId, ct);
            return TaoResponse(x == null ? XacMinhDanhTinh.Tao(userId, _clock.GetUtcNow()) : x);
        }

        public async Task<IdentityVerificationResponse> CapNhatAsync(Guid userId, UpdateIdentityVerificationRequest body, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            string so = XacMinhDanhTinh.ChuanHoaSoGiayTo(body.IdNumber);
            XacMinhDanhTinh x = await LayHoacTaoAsync(userId, ct);
            DateTimeOffset bayGio = _clock.GetUtcNow();
            x.CapNhat(body.FullName, body.DateOfBirth, so, _khoa.BamCoKhoa(so), HomNayVietNam(bayGio), bayGio);

            await _db.SaveChangesAsync(ct);
            return TaoResponse(x);
        }

        public async Task<LicenseUploadResponse> XinLinkTaiAnhAsync(Guid userId, IdentityDocumentUploadRequest body, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            string mat = (body.Side ?? string.Empty).Trim().ToLowerInvariant();
            if (mat != "front" && mat != "back")
            {
                throw new InvalidValueException("mat_giay_to_khong_hop_le", "side: front | back.");
            }

            string ten = Path.GetFileName((body.FileName ?? string.Empty).Trim());
            string duoi = Path.GetExtension(ten);
            if (ten.Length == 0 || ten.Length > 200 || !DuoiChoPhep.Contains(duoi))
            {
                throw new InvalidValueException("file_khong_hop_le", "Anh giay to phai la .png hoac .jpg.");
            }

            long toiDa = _settings.SoLon(SettingKeys.IdentityKybFileMaxBytes);
            if (body.SizeBytes <= 0 || body.SizeBytes > toiDa)
            {
                throw new InvalidValueException("file_qua_lon", "Anh giay to toi da " + toiDa + " byte.");
            }

            XacMinhDanhTinh x = await LayHoacTaoAsync(userId, ct);
            string khoa = "idv/" + userId + "/" + mat + "-" + Guid.CreateVersion7() + duoi.ToLowerInvariant();
            x.GanAnh(mat == "front", khoa, _clock.GetUtcNow());
            await _db.SaveChangesAsync(ct);

            return new LicenseUploadResponse
            {
                UploadUrl = await _storage.TaoLinkTaiLenAsync(khoa),
                Method = "PUT",
                ExpiresAt = _clock.GetUtcNow() + _settings.ThoiGian(SettingKeys.UploadLinkTtl),
            };
        }

        public async Task<IdentityVerificationResponse> NopAsync(Caller caller, CancellationToken ct)
        {
            Guid userId = caller.LayUserId();
            XacMinhDanhTinh x = await LayHoacTaoAsync(userId, ct);

            // Anh phai THAT SU nam trong kho (client co the xin link roi khong tai).
            foreach (string? khoa in new string?[] { x.FrontKey, x.BackKey })
            {
                if (khoa == null)
                {
                    continue;
                }

                ThongTinFile? file = await _storage.ThongTinAsync(khoa, ct);
                if (file == null)
                {
                    throw new RuleViolationException("thieu_anh_giay_to", "Chua tai anh giay to len (link da cap nhung chua co file).");
                }

                if (file.SizeBytes > _settings.SoLon(SettingKeys.IdentityKybFileMaxBytes))
                {
                    throw new InvalidValueException("file_qua_lon", "Anh giay to vuot kich thuoc cho phep.");
                }
            }

            x.Nop(_clock.GetUtcNow());
            Phat(caller, new IdentityVerificationSubmitted { UserId = userId, FullName = x.FullName });
            await _db.SaveChangesAsync(ct);
            return TaoResponse(x);
        }

        // =====================================================================
        // ADMIN
        // =====================================================================

        public async Task<PagedResponse<IdentityVerificationAdminResponse>> HangDoiAsync(string? status, int page, int pageSize, CancellationToken ct)
        {
            int trang = page < 1 ? 1 : page;
            int co = pageSize < 1 || pageSize > 100 ? 20 : pageSize;

            KybStatus loc = KybStatus.Submitted;
            if (!string.IsNullOrWhiteSpace(status) && !Enum.TryParse(status, true, out loc))
            {
                throw new InvalidValueException("trang_thai_khong_hop_le", "status: draft | submitted | verified | rejected.");
            }

            IQueryable<XacMinhDanhTinh> q = _db.XacMinhDanhTinhs.AsNoTracking().Where(x => x.Status == loc);
            int tong = await q.CountAsync(ct);
            List<XacMinhDanhTinh> ds = await q.OrderBy(x => x.SubmittedAt).Skip((trang - 1) * co).Take(co).ToListAsync(ct);

            List<Guid> ids = ds.Select(x => x.UserId).ToList();
            Dictionary<Guid, User> users = await _db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);

            List<IdentityVerificationAdminResponse> items = new List<IdentityVerificationAdminResponse>();
            foreach (XacMinhDanhTinh x in ds)
            {
                User? u;
                if (users.TryGetValue(x.UserId, out u))
                {
                    items.Add(TaoAdminResponse(x, u, null, null, await TimTrungAsync(x, ct)));
                }
            }

            return new PagedResponse<IdentityVerificationAdminResponse> { Items = items, Page = trang, PageSize = co, Total = tong };
        }

        public async Task<IdentityVerificationAdminResponse> ChiTietAsync(Guid userId, CancellationToken ct)
        {
            return await ChiTietAsync(userId, null, ct);
        }

        /// <summary>caller != null: ghi audit lan xem anh giay to (S-07).</summary>
        public async Task<IdentityVerificationAdminResponse> ChiTietAsync(Guid userId, Caller? caller, CancellationToken ct)
        {
            XacMinhDanhTinh? x = await _db.XacMinhDanhTinhs.AsNoTracking().FirstOrDefaultAsync(v => v.UserId == userId, ct);
            User? u = await _db.Users.AsNoTracking().FirstOrDefaultAsync(v => v.Id == userId, ct);
            if (x == null || u == null)
            {
                throw new NotFoundException("Khong tim thay ho so xac minh danh tinh.");
            }

            string? truoc = x.FrontKey == null ? null : await _storage.TaoLinkXemAsync(x.FrontKey);
            string? sau = x.BackKey == null ? null : await _storage.TaoLinkXemAsync(x.BackKey);
            int soLink = (truoc == null ? 0 : 1) + (sau == null ? 0 : 1);
            if (soLink > 0 && caller != null)
            {
                Phat(caller, new SensitiveDocumentViewed { SubjectUserId = userId, DocumentKind = "identity_document", LinkCount = soLink });
                await _db.SaveChangesAsync(ct);
            }

            return TaoAdminResponse(x, u, truoc, sau, await TimTrungAsync(x, ct));
        }

        public async Task<IdentityVerificationResponse> DuyetAsync(Guid userId, Caller caller, CancellationToken ct)
        {
            XacMinhDanhTinh x = await LayCoSanAsync(userId, ct);
            Guid? trung = await TimTrungAsync(x, ct);
            if (trung.HasValue)
            {
                throw new RuleViolationException("giay_to_da_dung", "Giay to nay da xac minh cho tai khoan khac (" + trung.Value + ").");
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            x.Duyet(caller.LayUserId(), bayGio);
            Phat(caller, new UserIdentityVerified { UserId = userId, VerifiedAt = bayGio });

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex, "ux_identity_verifications_giay_to"))
            {
                // Hai admin duyet hai ho so cung giay to cung luc: UNIQUE tung phan chon mot ben.
                throw new RuleViolationException("giay_to_da_dung", "Giay to nay vua duoc xac minh cho tai khoan khac.");
            }

            return TaoResponse(x);
        }

        public async Task<IdentityVerificationResponse> TuChoiAsync(Guid userId, string? lyDo, Caller caller, CancellationToken ct)
        {
            XacMinhDanhTinh x = await LayCoSanAsync(userId, ct);
            x.TuChoi(caller.LayUserId(), lyDo, _clock.GetUtcNow());
            Phat(caller, new UserIdentityRejected { UserId = userId, Reason = x.RejectReason! });
            await _db.SaveChangesAsync(ct);
            return TaoResponse(x);
        }

        // =====================================================================

        private async Task<Guid?> TimTrungAsync(XacMinhDanhTinh x, CancellationToken ct)
        {
            if (x.IdNumberHash.Length == 0)
            {
                return null;
            }

            Guid khac = await _db.XacMinhDanhTinhs.AsNoTracking()
                .Where(v => v.IdNumberHash == x.IdNumberHash && v.Status == KybStatus.Verified && v.UserId != x.UserId)
                .Select(v => v.UserId)
                .FirstOrDefaultAsync(ct);
            return khac == Guid.Empty ? null : khac;
        }

        private async Task<XacMinhDanhTinh> LayHoacTaoAsync(Guid userId, CancellationToken ct)
        {
            XacMinhDanhTinh? x = await _db.XacMinhDanhTinhs.FirstOrDefaultAsync(v => v.UserId == userId, ct);
            if (x == null)
            {
                x = XacMinhDanhTinh.Tao(userId, _clock.GetUtcNow());
                _db.XacMinhDanhTinhs.Add(x);
            }

            return x;
        }

        private async Task<XacMinhDanhTinh> LayCoSanAsync(Guid userId, CancellationToken ct)
        {
            XacMinhDanhTinh? x = await _db.XacMinhDanhTinhs.FirstOrDefaultAsync(v => v.UserId == userId, ct);
            if (x == null)
            {
                throw new NotFoundException("Khong tim thay ho so xac minh danh tinh.");
            }

            return x;
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

        private IdentityVerificationResponse TaoResponse(XacMinhDanhTinh x)
        {
            return new IdentityVerificationResponse
            {
                UserId = x.UserId,
                Status = KybService.TenTrangThai(x.Status),
                FullName = x.FullName,
                DateOfBirth = x.DateOfBirth,
                IdNumberLast4 = x.IdNumberLast4.Length == 0 ? null : x.IdNumberLast4,
                HasFront = x.FrontKey != null,
                HasBack = x.BackKey != null,
                Adult = x.DuTuoi(_settings.SoNguyen(SettingKeys.IdentityAdultAge), HomNayVietNam(_clock.GetUtcNow())),
                RejectReason = x.RejectReason,
                SubmittedAt = x.SubmittedAt,
                ReviewedAt = x.ReviewedAt,
            };
        }

        private IdentityVerificationAdminResponse TaoAdminResponse(XacMinhDanhTinh x, User u, string? truoc, string? sau, Guid? trung)
        {
            return new IdentityVerificationAdminResponse
            {
                Verification = TaoResponse(x),
                Email = u.Email,
                DisplayName = u.DisplayName,
                Age = x.DateOfBirth.HasValue ? XacMinhDanhTinh.TinhTuoi(x.DateOfBirth.Value, HomNayVietNam(_clock.GetUtcNow())) : null,
                FrontUrl = truoc,
                BackUrl = sau,
                DuplicateOfUserId = trung,
            };
        }
    }
}
