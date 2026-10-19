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
    ///   Cong thanh toan (sandbox / VNPay / MoMo — doanh nghiep chon): tao lenh nap → doanh nghiep tra
    ///   tien tren trang cong → cong goi IPN / webhook CO CHU KY → payment HOI NGUOC cong xac nhan so
    ///   tien (VD-S-02) → phat deposit.confirmed → ledger ghi so. IPN bi lac thi worker doi soat hoi
    ///   nguoc cong cac lenh treo (VD-M-09); doanh nghiep cung bam "kiem tra" duoc (POST .../sync).
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
        private readonly PaymentDbContext _db;
        private readonly DanhBaCongNap _congNap;
        private readonly PaymentEventPublisher _events;
        private readonly TimeProvider _clock;
        private readonly ILogger<DepositService> _logger;
        private readonly ISettings _settings;

        public DepositService(
            PaymentDbContext db,
            DanhBaCongNap congNap,
            PaymentEventPublisher events,
            TimeProvider clock,
            ILogger<DepositService> logger,
            ISettings settings)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (congNap == null)
            {
                throw new ArgumentNullException(nameof(congNap));
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
            _congNap = congNap;
            _events = events;
            _clock = clock;
            _logger = logger;
            _settings = settings;
        }

        // =====================================================================
        // DOANH NGHIEP
        // =====================================================================

        /// <summary>
        /// Cac cong nap dang dung duoc: CO cau hinh khoa VA nam trong setting payment.enabled_providers
        /// (+ chuyen khoan thu cong neu bat). Frontend hien danh sach nay cho doanh nghiep chon.
        /// </summary>
        public List<string> CacCongDangMo()
        {
            HashSet<string> batTrongSetting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string t in _settings.Chuoi(SettingKeys.PaymentEnabledProviders).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                batTrongSetting.Add(t);
            }

            List<string> ds = new List<string>();
            foreach (string ten in _congNap.TenCacCong)
            {
                if (batTrongSetting.Contains(ten))
                {
                    ds.Add(ten);
                }
            }

            ds.Sort(StringComparer.Ordinal);
            if (_settings.DungSai(SettingKeys.PaymentManualTransferEnabled))
            {
                ds.Add(PaymentIntent.ChuyenKhoanThuCong);
            }

            return ds;
        }

        /// <summary>
        /// Tao lenh nap qua cong. Ba pha (VD-M-09): ghi lenh Pending va COMMIT → goi cong (khong
        /// transaction nao dang mo) → ghi link / loi trong transaction moi. Cong loi luc tao → lenh
        /// Failed (cong_loi), tra 502 — bam lai cung Idempotency-Key thi nhan lai lenh that bai do,
        /// dung khoa moi de thu lai.
        /// </summary>
        public async Task<DepositResponse> TaoAsync(long amountVnd, string? tenCong, string? ipKhach, string? idempotencyKey, Caller caller, CancellationToken ct)
        {
            string ten = string.IsNullOrWhiteSpace(tenCong) ? "sandbox" : tenCong.Trim().ToLowerInvariant();
            ICongNap? cong = _congNap.Tim(ten);
            if (cong == null || ten == PaymentIntent.ChuyenKhoanThuCong || !CacCongDangMo().Contains(ten))
            {
                throw new InvalidValueException("cong_khong_ho_tro", "Cong '" + ten + "' khong mo. Cong dang mo: " + string.Join(", ", CacCongDangMo()) + ".");
            }

            Guid businessId = caller.LayUserId();
            string khoa = idempotencyKey ?? string.Empty;
            PaymentIntent? cu = await _db.Intents.AsNoTracking().FirstOrDefaultAsync(i => i.BusinessId == businessId && i.IdempotencyKey == khoa, ct);
            if (cu != null)
            {
                return TaoResponse(cu);
            }

            DepositResponse moi = await TaoTheoKhoaAsync(
                businessId,
                khoa,
                k => PaymentIntent.Tao(businessId, amountVnd, cong.Name, k, QuyDinhNap(), _clock.GetUtcNow()),
                ct);

            PaymentIntent intent = await _db.Intents.FirstAsync(i => i.Id == moi.IntentId, ct);
            if (intent.CheckoutUrl != null || intent.Status != PaymentIntentStatus.Pending)
            {
                // Request song song cung khoa da tao xong link.
                return TaoResponse(intent);
            }

            string link;
            try
            {
                link = await cong.TaoLinkThanhToanAsync(YeuCau(intent, ipKhach), ct);
            }
            catch (CongThanhToanException ex)
            {
                _logger.LogWarning(ex, "Cong {Cong} loi khi tao lenh nap {IntentId}", cong.Name, intent.Id);
                intent.ThatBai(_clock.GetUtcNow(), "cong_loi");
                await _db.SaveChangesAsync(ct);
                throw;
            }

            intent.GanLinkThanhToan(link);
            await _db.SaveChangesAsync(ct);
            return TaoResponse(intent);
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

        /// <summary>
        /// POST /payments/deposits/{id}/sync — doanh nghiep vua tra xong (trinh duyet quay ve tu cong) bam
        /// "kiem tra": payment hoi nguoc cong ngay thay vi doi IPN / worker doi soat. Dung khi IPN khong
        /// toi duoc (dev chay localhost, cong khong goi vao duoc).
        /// </summary>
        public async Task<DepositResponse> DongBoAsync(Guid intentId, Caller caller, CancellationToken ct)
        {
            Guid uid = caller.LayUserId();
            PaymentIntent? i = await _db.Intents.FirstOrDefaultAsync(x => x.Id == intentId, ct);
            if (i == null || i.BusinessId != uid)
            {
                throw new NotFoundException("Khong tim thay lenh nap.");
            }

            if (i.Status == PaymentIntentStatus.Pending && !i.LaChuyenKhoanThuCong)
            {
                await HoiNguocVaChotAsync(i, false, caller.CorrelationId, ct);
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
                .OrderByDescending(i => i.CreatedAt)
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
        // IPN / WEBHOOK CONG THANH TOAN
        // =====================================================================

        /// <summary>
        /// Xu ly thong bao ket qua cua cong. Adapter DA kiem chu ky (thongBao = null nghia la chu ky sai).
        /// Thanh cong thi HOI NGUOC cong (VD-S-02): chu ky dung chi chung minh tin den tu cong, hoi nguoc
        /// chung minh giao dich con hieu luc va dung so tien. Sandbox khong tra cuu duoc → tin chu ky.
        /// Thong bao lap lai (cong gui nhieu lan) → lan sau khong lam gi.
        /// Loi nem ra: ForbiddenException chu_ky_sai | NotFoundException | RuleViolationException
        /// (sai_cong, sai_so_tien, da_thanh_toan, lenh_nap_da_chot, cong_khong_xac_nhan) |
        /// CongChuaXacNhanException (hoi nguoc chua duoc — cong nen gui lai).
        /// </summary>
        public async Task<bool> XuLyThongBaoAsync(ICongNap cong, ThongBaoNap? thongBao, Guid correlationId, CancellationToken ct)
        {
            if (cong == null)
            {
                throw new ArgumentNullException(nameof(cong));
            }

            if (thongBao == null)
            {
                _logger.LogWarning("Thong bao cong {Cong} chu ky SAI — tu choi. Co the la gia mao.", cong.Name);
                throw new ForbiddenException("chu_ky_sai", "Chu ky thong bao khong hop le.");
            }

            PaymentIntent? intent = await _db.Intents.FirstOrDefaultAsync(i => i.Id == thongBao.IntentId, ct);
            if (intent == null)
            {
                throw new NotFoundException("Khong tim thay lenh nap.");
            }

            // Lenh cua cong KHAC (vd chuyen khoan thu cong) khong duoc chot bang thong bao cong nay.
            if (!string.Equals(intent.Provider, cong.Name, StringComparison.Ordinal))
            {
                throw new RuleViolationException("sai_cong", "Lenh nap khong thuoc cong " + cong.Name + ".");
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            if (!thongBao.ThanhCong)
            {
                bool dangCho = intent.Status == PaymentIntentStatus.Pending;
                intent.ThatBai(bayGio, "cong_" + thongBao.MaKetQua);
                await _db.SaveChangesAsync(ct);
                return dangCho;
            }

            if (intent.Status == PaymentIntentStatus.Succeeded)
            {
                // IPN lap: XacNhanThanhCong tra false (cung ma) hoac nem da_thanh_toan (ma khac).
                intent.XacNhanThanhCong(thongBao.ProviderTxnId, thongBao.AmountVnd, bayGio);
                return false;
            }

            // So tien thong bao phai khop lenh — chan som truoc khi hoi nguoc.
            if (thongBao.AmountVnd != intent.AmountVnd)
            {
                throw new RuleViolationException("sai_so_tien", "Cong bao " + thongBao.AmountVnd + "d nhung lenh nap la " + intent.AmountVnd + "d.");
            }

            KetQuaTraCuuNap kq = await cong.TraCuuNapAsync(YeuCau(intent, null), ct);
            if (kq.TrangThai == TrangThaiTraCuu.KhongRo && cong is SandboxPaymentProvider)
            {
                return await ChotThanhCongAsync(intent, thongBao.ProviderTxnId, thongBao.AmountVnd, correlationId, ct);
            }

            if (kq.TrangThai == TrangThaiTraCuu.KhongRo)
            {
                // Chua hoi duoc: KHONG cong tien theo mot minh IPN. Cong gui lai IPN / worker doi soat se chot.
                throw new CongChuaXacNhanException("Chua hoi nguoc duoc cong " + cong.Name + " (" + kq.MaKetQua + ").");
            }

            if (kq.TrangThai != TrangThaiTraCuu.DaThanhToan)
            {
                _logger.LogWarning(
                    "IPN {Cong} bao thanh cong lenh {IntentId} nhung tra cuu noi {TrangThai} ({Ma}) — KHONG cong tien",
                    cong.Name, intent.Id, kq.TrangThai, kq.MaKetQua);
                throw new RuleViolationException("cong_khong_xac_nhan", "Cong khong xac nhan giao dich nay.");
            }

            return await ChotThanhCongAsync(intent, kq.ProviderTxnId.Length > 0 ? kq.ProviderTxnId : thongBao.ProviderTxnId, kq.AmountVnd, correlationId, ct);
        }

        /// <summary>
        /// Doi soat (VD-M-09): lenh nap qua cong Pending lau hon payment.deposit_lookup_after → hoi nguoc
        /// cong. Da tra → chot; that bai → Failed; chua ro / chua tra ma qua payment.deposit_expire_after
        /// → Failed (het_han). Tra so lenh da chot (thanh cong + that bai).
        /// </summary>
        public async Task<int> DoiSoatAsync(CancellationToken ct)
        {
            DateTimeOffset bayGio = _clock.GetUtcNow();
            TimeSpan sau = _settings.ThoiGian(SettingKeys.PaymentDepositLookupAfter);
            DateTimeOffset moc = bayGio - sau;
            int lo = _settings.SoNguyen(SettingKeys.PaymentDepositReconcileBatch);

            List<Guid> ids = await _db.Intents.AsNoTracking()
                .Where(i => i.Status == PaymentIntentStatus.Pending
                    && i.Provider != PaymentIntent.ChuyenKhoanThuCong
                    && i.CreatedAt < moc
                    && (i.LastCheckedAt == null || i.LastCheckedAt < moc))
                .OrderBy(i => i.CreatedAt)
                .Select(i => i.Id)
                .Take(lo)
                .ToListAsync(ct);

            int daChot = 0;
            foreach (Guid id in ids)
            {
                _db.ChangeTracker.Clear();
                PaymentIntent? i = await _db.Intents.FirstOrDefaultAsync(x => x.Id == id, ct);
                if (i == null || i.Status != PaymentIntentStatus.Pending)
                {
                    continue;
                }

                try
                {
                    if (await HoiNguocVaChotAsync(i, true, Guid.CreateVersion7(), ct))
                    {
                        daChot++;
                    }
                }
                catch (Exception ex) when (ex is RuleViolationException || ex is DbUpdateConcurrencyException)
                {
                    _logger.LogWarning(ex, "Doi soat lenh nap {IntentId} loi — de lan sau", id);
                }
            }

            return daChot;
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

        /// <summary>Hoi nguoc cong ve mot lenh Pending roi chot neu co ket luan. Tra true neu da chot.</summary>
        private async Task<bool> HoiNguocVaChotAsync(PaymentIntent i, bool laDoiSoat, Guid correlationId, CancellationToken ct)
        {
            DateTimeOffset bayGio = _clock.GetUtcNow();
            ICongNap? cong = _congNap.Tim(i.Provider);
            KetQuaTraCuuNap kq = cong == null
                ? KetQuaTraCuuNap.KhongRo("cong_khong_con_cau_hinh")
                : await cong.TraCuuNapAsync(YeuCau(i, null), ct);

            if (kq.TrangThai == TrangThaiTraCuu.DaThanhToan)
            {
                if (kq.AmountVnd != i.AmountVnd)
                {
                    _logger.LogError("Tra cuu {Cong} lenh {IntentId}: cong thu {CongThu}d, lenh {Lenh}d — KHONG cong tien, can admin xem",
                        i.Provider, i.Id, kq.AmountVnd, i.AmountVnd);
                    i.DanhDauDaHoi(bayGio);
                    await _db.SaveChangesAsync(ct);
                    return false;
                }

                await ChotThanhCongAsync(i, kq.ProviderTxnId, kq.AmountVnd, correlationId, ct);
                return true;
            }

            if (kq.TrangThai == TrangThaiTraCuu.ThatBai)
            {
                i.ThatBai(bayGio, "cong_" + kq.MaKetQua);
                await _db.SaveChangesAsync(ct);
                return true;
            }

            if (laDoiSoat && i.CreatedAt < bayGio - _settings.ThoiGian(SettingKeys.PaymentDepositExpireAfter))
            {
                i.ThatBai(bayGio, "het_han");
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation("Lenh nap {IntentId} ({Cong}) het han — {Ma}", i.Id, i.Provider, kq.MaKetQua);
                return true;
            }

            i.DanhDauDaHoi(bayGio);
            await _db.SaveChangesAsync(ct);
            return false;
        }

        /// <summary>
        /// Ghi thanh cong + phat deposit.confirmed trong CUNG transaction (outbox). IPN va worker doi soat
        /// chot cung luc → RowVersion chan mot ben (DbUpdateConcurrencyException); ben thua doc lai thay
        /// lenh da Succeeded voi cung ma giao dich thi coi nhu xong.
        /// </summary>
        private async Task<bool> ChotThanhCongAsync(PaymentIntent i, string maGiaoDich, long soTien, Guid correlationId, CancellationToken ct)
        {
            if (!i.XacNhanThanhCong(maGiaoDich, soTien, _clock.GetUtcNow()))
            {
                return false;
            }

            PhatDaNap(i, Caller.HeThong(correlationId, null));
            try
            {
                await LuuAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                _db.ChangeTracker.Clear();
                PaymentIntent? lai = await _db.Intents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == i.Id, ct);
                if (lai != null && lai.Status == PaymentIntentStatus.Succeeded && lai.ProviderTxnId == maGiaoDich)
                {
                    _logger.LogInformation("Lenh nap {IntentId} vua duoc chot o luong khac — bo qua", i.Id);
                    return false;
                }

                throw;
            }

            return true;
        }

        private static YeuCauThanhToan YeuCau(PaymentIntent i, string? ipKhach)
        {
            return new YeuCauThanhToan
            {
                IntentId = i.Id,
                AmountVnd = i.AmountVnd,
                CreatedAt = i.CreatedAt,
                ClientIp = string.IsNullOrWhiteSpace(ipKhach) ? "127.0.0.1" : ipKhach,
                MoTa = "Nap tien vi DATN " + i.Id.ToString("N").Substring(24).ToUpperInvariant(),
            };
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
                CheckoutUrl = thuCong ? null : i.CheckoutUrl,
                FailureCode = i.FailureCode,
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
