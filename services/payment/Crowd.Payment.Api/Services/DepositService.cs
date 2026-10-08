using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Persistence;
using Crowd.BuildingBlocks.Settings;
using Crowd.Contracts.Payment;
using Crowd.Payment.Api.Dtos;
using Crowd.Payment.Api.Exceptions;
using Crowd.Payment.Domain.Common;
using Crowd.Payment.Domain.Deposits;
using Crowd.Payment.Infrastructure.Persistence;
using Crowd.Payment.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crowd.Payment.Api.Services
{
    /// <summary>
    /// Nap tien (FB-03), hai duong:
    ///
    ///   Cong thanh toan: tao lenh nap → doanh nghiep tra tien tren trang cong →
    ///   cong goi WEBHOOK co chu ky → phat deposit.confirmed → ledger ghi so.
    ///
    ///   Chuyen khoan thu cong (setting payment.manual_transfer_enabled): tao lenh
    ///   → doanh nghiep chuyen khoan voi noi dung = ma lenh → bao "da chuyen" →
    ///   ADMIN doi chieu sao ke roi duyet → deposit.confirmed. So tien khong vuot
    ///   payment.manual_transfer_auto_approve_max_vnd thi he thong duyet luon
    ///   (mac dinh 0 = luon cho admin).
    ///
    /// Client khong co cach nao tu bao "toi da tra roi" ma tien vao so.
    /// </summary>
    public sealed class DepositService
    {
        private static readonly JsonSerializerOptions JsonWebhook = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        private readonly PaymentDbContext _db;
        private readonly IPaymentProvider _provider;
        private readonly PaymentEventPublisher _events;
        private readonly TimeProvider _clock;
        private readonly ILogger<DepositService> _logger;
        private readonly ISettings _settings;

        public DepositService(
            PaymentDbContext db,
            IPaymentProvider provider,
            PaymentEventPublisher events,
            TimeProvider clock,
            ILogger<DepositService> logger,
            ISettings settings)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            _db = db;
            _provider = provider;
            _events = events;
            _clock = clock;
            _logger = logger;
            _settings = settings;
        }

        // =====================================================================
        // DOANH NGHIEP
        // =====================================================================

        public async Task<DepositResponse> TaoAsync(long amountVnd, string? idempotencyKey, Caller caller, CancellationToken ct)
        {
            Guid businessId = caller.LayUserId();
            return await TaoTheoKhoaAsync(
                businessId,
                idempotencyKey ?? string.Empty,
                khoa => PaymentIntent.Tao(businessId, amountVnd, _provider.Name, khoa, QuyDinhNap(), _clock.GetUtcNow()),
                ct);
        }

        /// <summary>POST /payments/deposits/manual — tra ve thong tin tai khoan + ma noi dung chuyen khoan.</summary>
        public async Task<DepositResponse> TaoChuyenKhoanAsync(long amountVnd, string? idempotencyKey, Caller caller, CancellationToken ct)
        {
            if (!_settings.DungSai(SettingKeys.PaymentManualTransferEnabled))
            {
                throw new RuleViolationException("chuyen_khoan_tat", "Nen tang dang tat nap tien bang chuyen khoan thu cong.");
            }

            Guid businessId = caller.LayUserId();
            return await TaoTheoKhoaAsync(
                businessId,
                idempotencyKey ?? string.Empty,
                khoa => PaymentIntent.TaoChuyenKhoan(businessId, amountVnd, khoa, QuyDinhNap(), _clock.GetUtcNow()),
                ct);
        }

        /// <summary>
        /// Doanh nghiep bao "da chuyen khoan". Duoi nguong tu duyet thi tien vao vi
        /// ngay; khong thi cho admin doi chieu.
        /// </summary>
        public async Task<DepositResponse> BaoDaChuyenAsync(Guid intentId, Caller caller, CancellationToken ct)
        {
            Guid uid = caller.LayUserId();
            PaymentIntent? i = await _db.Intents.FirstOrDefaultAsync(x => x.Id == intentId, ct);
            if (i == null || i.BusinessId != uid)
            {
                throw new NotFoundException("Khong tim thay lenh nap.");
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            i.BaoDaChuyen(bayGio);

            if (i.Status == PaymentIntentStatus.AwaitingApproval
                && i.AmountVnd <= _settings.SoLon(SettingKeys.PaymentManualTransferAutoApproveMaxVnd))
            {
                i.DuyetChuyenKhoan(null, null, bayGio);
                PhatDaNap(i, Caller.HeThong(caller.CorrelationId, null));
                _logger.LogInformation("Lenh nap chuyen khoan {IntentId} tu duyet (duoi nguong)", i.Id);
            }

            await LuuAsync(ct);
            return TaoResponse(i);
        }

        public async Task<DepositResponse> XemAsync(Guid intentId, Caller caller, CancellationToken ct)
        {
            Guid uid = caller.LayUserId();
            PaymentIntent? i = await _db.Intents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == intentId, ct);

            // Lenh nap cua nguoi khac → 404 (BOLA).
            if (i == null || (i.BusinessId != uid && !caller.IsAdmin))
            {
                throw new NotFoundException("Khong tim thay lenh nap.");
            }

            return TaoResponse(i);
        }

        // =====================================================================
        // ADMIN — doi chieu chuyen khoan thu cong
        // =====================================================================

        /// <summary>Mac dinh lenh CHO DUYET (doanh nghiep da bao chuyen), cu nhat truoc.</summary>
        public async Task<PagedResponse<DepositResponse>> DanhSachChuyenKhoanAsync(
            PaymentIntentStatus? status, int page, int pageSize, CancellationToken ct)
        {
            if (page < 1)
            {
                page = 1;
            }

            if (pageSize < 1 || pageSize > 100)
            {
                pageSize = 20;
            }

            PaymentIntentStatus loc = status.HasValue ? status.Value : PaymentIntentStatus.AwaitingApproval;
            IQueryable<PaymentIntent> q = _db.Intents.AsNoTracking()
                .Where(i => i.Provider == PaymentIntent.ChuyenKhoanThuCong && i.Status == loc);

            int tong = await q.CountAsync(ct);
            List<PaymentIntent> ds = await q
                .OrderBy(i => i.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            return new PagedResponse<DepositResponse>
            {
                Items = ds.Select(TaoResponse).ToList(),
                Page = page,
                PageSize = pageSize,
                Total = tong,
            };
        }

        /// <summary>Admin thay tien da ve (doi chieu sao ke) → deposit.confirmed, ledger cong vi.</summary>
        public async Task<DepositResponse> DuyetChuyenKhoanAsync(Guid intentId, string? maGiaoDich, Caller caller, CancellationToken ct)
        {
            PaymentIntent i = await LayChuyenKhoanAsync(intentId, ct);
            i.DuyetChuyenKhoan(caller.LayUserId(), maGiaoDich, _clock.GetUtcNow());
            PhatDaNap(i, caller);

            await LuuAsync(ct);
            return TaoResponse(i);
        }

        public async Task<DepositResponse> TuChoiChuyenKhoanAsync(Guid intentId, string? lyDo, Caller caller, CancellationToken ct)
        {
            PaymentIntent i = await LayChuyenKhoanAsync(intentId, ct);
            i.TuChoiChuyenKhoan(caller.LayUserId(), lyDo ?? string.Empty, _clock.GetUtcNow());

            await LuuAsync(ct);
            return TaoResponse(i);
        }

        // =====================================================================
        // WEBHOOK CONG THANH TOAN
        // =====================================================================

        /// <summary>
        /// Xu ly webhook. THU TU QUAN TRONG: kiem chu ky TRUOC khi doc bat cu gi.
        /// Webhook lap lai (cong gui 3 lan) → lan 2, 3 khong lam gi, van tra 200 de
        /// cong ngung gui lai.
        /// </summary>
        public async Task XuLyWebhookAsync(string body, string? chuKy, Guid correlationId, CancellationToken ct)
        {
            if (!_provider.KiemChuKy(body, chuKy))
            {
                _logger.LogWarning("Webhook chu ky SAI — tu choi. Co the la gia mao.");
                throw new ForbiddenException("chu_ky_sai", "Chu ky webhook khong hop le.");
            }

            SandboxWebhookBody? w = JsonSerializer.Deserialize<SandboxWebhookBody>(body, JsonWebhook);
            if (w == null)
            {
                throw new InvalidValueException("webhook_hong", "Webhook khong doc duoc.");
            }

            PaymentIntent? intent = await _db.Intents.FirstOrDefaultAsync(i => i.Id == w.IntentId, ct);
            if (intent == null)
            {
                throw new NotFoundException("Khong tim thay lenh nap.");
            }

            // Lenh cua cong KHAC (vd chuyen khoan thu cong) khong duoc chot bang webhook cong nay.
            if (!string.Equals(intent.Provider, _provider.Name, StringComparison.Ordinal))
            {
                throw new RuleViolationException("sai_cong", "Lenh nap khong thuoc cong " + _provider.Name + ".");
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();

            if (!string.Equals(w.Status, "success", StringComparison.OrdinalIgnoreCase))
            {
                intent.ThatBai(bayGio);
                await _db.SaveChangesAsync(ct);
                return;
            }

            bool moi = intent.XacNhanThanhCong(w.ProviderTxnId ?? string.Empty, w.AmountVnd, bayGio);
            if (!moi)
            {
                _logger.LogInformation("Webhook lap cho lenh nap {IntentId} — bo qua", intent.Id);
                return;
            }

            PhatDaNap(intent, Caller.HeThong(correlationId, null));
            await LuuAsync(ct);
        }

        // =====================================================================

        private QuyDinhNap QuyDinhNap()
        {
            return new QuyDinhNap
            {
                ToiThieuVnd = _settings.SoLon(SettingKeys.PaymentDepositMinVnd),
                ToiDaVnd = _settings.SoLon(SettingKeys.PaymentDepositMaxVnd),
            };
        }

        private async Task<DepositResponse> TaoTheoKhoaAsync(
            Guid businessId, string khoa, Func<string, PaymentIntent> tao, CancellationToken ct)
        {
            PaymentIntent? cu = await _db.Intents.AsNoTracking().FirstOrDefaultAsync(i => i.BusinessId == businessId && i.IdempotencyKey == khoa, ct);
            if (cu != null)
            {
                return TaoResponse(cu);
            }

            PaymentIntent moi = tao(khoa);
            _db.Intents.Add(moi);

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex, "ux_intents_idempotency"))
            {
                _db.ChangeTracker.Clear();
                PaymentIntent benThang = await _db.Intents.AsNoTracking().FirstAsync(i => i.BusinessId == businessId && i.IdempotencyKey == khoa, ct);
                return TaoResponse(benThang);
            }

            return TaoResponse(moi);
        }

        private async Task<PaymentIntent> LayChuyenKhoanAsync(Guid intentId, CancellationToken ct)
        {
            PaymentIntent? i = await _db.Intents.FirstOrDefaultAsync(x => x.Id == intentId, ct);
            if (i == null || !i.LaChuyenKhoanThuCong)
            {
                throw new NotFoundException("Khong tim thay lenh nap chuyen khoan.");
            }

            return i;
        }

        private void PhatDaNap(PaymentIntent i, Caller caller)
        {
            _events.Phat(caller, new DepositConfirmed
            {
                IntentId = i.Id,
                BusinessId = i.BusinessId,
                AmountVnd = i.AmountVnd,
                Provider = i.Provider,
                ProviderTxnId = i.ProviderTxnId!,
            });
        }

        private async Task LuuAsync(CancellationToken ct)
        {
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex, "ux_intents_provider_txn"))
            {
                // Mot ma giao dich (cong / sao ke) dung cho HAI lenh nap: gian lan hoac nham.
                throw new RuleViolationException("ma_giao_dich_trung", "Ma giao dich da duoc dung cho lenh nap khac.");
            }
        }

        private DepositResponse TaoResponse(PaymentIntent i)
        {
            bool thuCong = i.LaChuyenKhoanThuCong;
            return new DepositResponse
            {
                IntentId = i.Id,
                BusinessId = i.BusinessId,
                AmountVnd = i.AmountVnd,
                Provider = i.Provider,
                Status = i.Status,
                CheckoutUrl = thuCong ? null : _provider.TaoLinkThanhToan(i.Id, i.AmountVnd),
                TransferCode = i.TransferCode,
                BankInfo = thuCong ? _settings.Chuoi(SettingKeys.PaymentManualTransferBankInfo) : null,
                TransferredAt = i.TransferredAt,
                RejectReason = i.RejectReason,
                CreatedAt = i.CreatedAt,
                CompletedAt = i.CompletedAt,
            };
        }
    }
}
