using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Settings;
using Crowd.Contracts.Annotation;
using Crowd.Contracts.Gate;
using Crowd.Contracts.Ledger;
using Crowd.Contracts.Link;
using Crowd.Contracts.Payment;
using Crowd.Contracts.Project;
using Crowd.Ledger.Domain.Accounts;
using Crowd.Ledger.Domain.Common;
using Crowd.Ledger.Domain.Escrows;
using Crowd.Ledger.Domain.Holds;
using Crowd.Ledger.Domain.Journal;
using Crowd.Ledger.Domain.Withdrawals;
using Crowd.Ledger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crowd.Ledger.Api.Services
{
    /// <summary>
    /// MOI dong tien do event gay ra — docs 3.3 thanh code. Duoc goi tu consumer
    /// (ben trong transaction cua IdempotencyGuard) va tu worker giai phong treo.
    ///
    /// Moi ham: khoa so cai → doc so du neu can quyet dinh → ghi but toan qua
    /// LedgerWriter → phat event qua outbox. Tat ca trong MOT transaction.
    /// </summary>
    public sealed class MoneyFlowService
    {
        private readonly LedgerDbContext _db;
        private readonly LedgerWriter _writer;
        private readonly LedgerEventPublisher _events;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;
        private readonly ILogger<MoneyFlowService> _logger;

        public MoneyFlowService(
            LedgerDbContext db,
            LedgerWriter writer,
            LedgerEventPublisher events,
            ISettings settings,
            TimeProvider clock,
            ILogger<MoneyFlowService> logger)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _db = db;
            _writer = writer;
            _events = events;
            _settings = settings;
            _clock = clock;
            _logger = logger;
        }

        // =====================================================================
        // NAP TIEN (FB-03)
        // =====================================================================

        public async Task NapTienAsync(DepositConfirmed p, CancellationToken ct)
        {
            JournalEntry e = Postings.NapTien(p.IntentId, p.BusinessId, p.AmountVnd, p.Provider, _clock.GetUtcNow());
            bool moi = await _writer.GhiAsync(e, ct);

            if (moi)
            {
                _logger.LogInformation("Nap {Amount}d cho doanh nghiep {BusinessId}", p.AmountVnd, p.BusinessId);
            }
        }

        // =====================================================================
        // SAGA PUBLISH BUOC 2 (docs 3.4)
        // =====================================================================

        /// <summary>
        /// Du tien → giu ky quy + escrow.reserved. Thieu → escrow.rejected kem so
        /// cu the. Thieu tien la KET QUA NGHIEP VU binh thuong, khong phai loi —
        /// khong nem ngoai le (nem thi message quay vong vo ich roi vao DLQ).
        /// </summary>
        public async Task DatKyQuyAsync(ProjectPublishRequested p, Caller caller, CancellationToken ct)
        {
            await _writer.KhoaAsync(ct);

            if (await _db.Escrows.AnyAsync(x => x.ProjectId == p.ProjectId, ct))
            {
                _logger.LogInformation("Du an {ProjectId} da ky quy tu truoc — bo qua", p.ProjectId);
                return;
            }

            // Doc so du SAU khi khoa so cai: khong giao dich nao chen vao giua
            // "doc thay du" va "tru tien" (VD-M-01).
            long kheDung = await _writer.SoDuAsync(AccountCodes.BusinessAvailable(p.OwnerId), ct);

            if (kheDung < p.EscrowAmountVnd)
            {
                _events.Phat(caller, new EscrowRejected
                {
                    ProjectId = p.ProjectId,
                    Reason = "So du kha dung " + kheDung + "d, can ky quy " + p.EscrowAmountVnd + "d. Hay nap them tien.",
                });
                return;
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            await _writer.GhiAsync(Postings.DatKyQuy(p.ProjectId, p.OwnerId, p.EscrowAmountVnd, bayGio), ct);
            _db.Escrows.Add(ProjectEscrow.Tao(p.ProjectId, p.OwnerId, p.EscrowAmountVnd, bayGio));

            _events.Phat(caller, new EscrowReserved { ProjectId = p.ProjectId, AmountVnd = p.EscrowAmountVnd });
        }

        /// <summary>
        /// project.published: chot redundancy (chan chi vuot, VD-M-03) va dieu khoan de tinh
        /// ngan sach cong link. Du an bat cong link thi bao gate-svc ngan sach ban dau.
        /// </summary>
        public async Task GhiNhanRedundancyAsync(ProjectPublished p, Caller caller, CancellationToken ct)
        {
            ProjectEscrow? e = await _db.Escrows.FirstOrDefaultAsync(x => x.ProjectId == p.ProjectId, ct);
            if (e != null)
            {
                // TRAN redundancy, khong phai redundancy ban dau: quality-svc co the
                // xin them nguoi gan cho mau tranh chap (docs 3.7). Ky quy da tinh
                // theo tran nen chi toi tran van du tien; qua tran moi la loi.
                e.DatRedundancy(p.MaxRedundancy);
                e.GhiDieuKhoan(p.SampleCount, p.UnitPriceVnd, p.PlatformFeeVnd, p.AllowLinkGateway);

                if (p.AllowLinkGateway)
                {
                    PhatNganSachCong(e, caller);
                }
            }
        }

        // =====================================================================
        // CONG LINK (P2)
        // =====================================================================

        /// <summary>
        /// click.validated: ky quy → sharer TREO + nen tang, chi trong phan ngan sach cong link
        /// (khong an vao tien danh cho labeler). Het ngan sach thi KHONG chi — la ket qua nghiep
        /// vu binh thuong (gate phuc vu theo ban sao co do tre), khong nem loi: chi bao lai
        /// ngan sach de gate ngung phuc vu.
        /// </summary>
        public async Task ChiTraCongLinkAsync(ClickValidated c, Caller caller, CancellationToken ct)
        {
            await _writer.KhoaAsync(ct);

            if (await _db.Holds.AnyAsync(h => h.AnnotationId == c.ClickId && h.Kind == HoldKind.GateClick, ct))
            {
                _logger.LogInformation("Luot {ClickId} da chi tu truoc — bo qua", c.ClickId);
                return;
            }

            ProjectEscrow? escrow = await _db.Escrows.FirstOrDefaultAsync(x => x.ProjectId == c.ProjectId, ct);
            if (escrow == null)
            {
                throw new RuleViolationException("chua_ky_quy", "Du an " + c.ProjectId + " chua co ky quy — khong chi duoc.");
            }

            long tong = checked(c.SharerAmountVnd + c.PlatformAmountVnd);
            if (escrow.NganSachCongConLai() < tong)
            {
                _logger.LogWarning(
                    "Du an {ProjectId} het ngan sach cong link (con {ConLai}d, can {Can}d) — khong chi luot {ClickId}",
                    c.ProjectId, escrow.NganSachCongConLai(), tong, c.ClickId);
                PhatNganSachCong(escrow, caller);
                return;
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            TimeSpan treo = _settings.ThoiGian(SettingKeys.LedgerHoldDuration);

            await _writer.GhiAsync(Postings.ChiTraCongLink(c.ClickId, c.ProjectId, c.SharerId, c.SharerAmountVnd, c.PlatformAmountVnd, bayGio), ct);
            escrow.TieuChoCongLink(tong);

            FundsHold hold = FundsHold.TaoChoCongLink(HoldKind.GateClick, c.ClickId, c.ProjectId, c.LinkId, c.SharerId, c.SharerAmountVnd, bayGio, treo);
            _db.Holds.Add(hold);
            _events.Phat(caller, new FundsHeld
            {
                HoldId = hold.Id,
                AnnotationId = c.ClickId,
                LabelerId = c.SharerId,
                AmountVnd = c.SharerAmountVnd,
                ReleaseAt = hold.ReleaseAt,
            });

            await ChiHoaHongAsync(c, treo, bayGio, caller, ct);
            PhatNganSachCong(escrow, caller);
        }

        /// <summary>
        /// FS-08: nguoi gioi thieu huong link.referral_percent doanh thu cong link cua nguoi duoc
        /// moi — LAY TU PHAN NEN TANG, khong tru vao nguoi duoc moi. Chi tra khi nguoi duoc moi
        /// da tu kiem >= link.referral_min_earnings_vnd (VD-L-05: clone tu moi nhau khong co gi).
        /// </summary>
        private async Task ChiHoaHongAsync(ClickValidated c, TimeSpan treo, DateTimeOffset bayGio, Caller caller, CancellationToken ct)
        {
            ReferralLink? r = await _db.Referrals.AsNoTracking().FirstOrDefaultAsync(x => x.ReferredId == c.SharerId, ct);
            int phanTram = _settings.SoNguyen(SettingKeys.LinkReferralPercent);
            if (r == null || phanTram <= 0)
            {
                return;
            }

            // Tong da kiem qua cong link (khoan dang treo + da giai phong, KHONG tinh khoan bi giu
            // lai vi vi pham), CHUA gom luot nay (chua SaveChanges).
            long daKiem = await _db.Holds
                .Where(h => h.LabelerId == c.SharerId && h.Kind == HoldKind.GateClick && h.State != HoldState.Withheld)
                .SumAsync(h => h.AmountVnd, ct);
            if (daKiem + c.SharerAmountVnd < _settings.SoLon(SettingKeys.LinkReferralMinEarningsVnd))
            {
                return;
            }

            long hoaHong = Math.Min(c.SharerAmountVnd * phanTram / 100, c.PlatformAmountVnd);
            if (hoaHong <= 0)
            {
                return;
            }

            await _writer.GhiAsync(Postings.HoaHongGioiThieu(c.ClickId, r.ReferrerId, hoaHong, bayGio), ct);
            FundsHold h = FundsHold.TaoChoCongLink(HoldKind.Referral, c.ClickId, c.ProjectId, c.LinkId, r.ReferrerId, hoaHong, bayGio, treo);
            _db.Holds.Add(h);
            _events.Phat(caller, new FundsHeld
            {
                HoldId = h.Id,
                AnnotationId = c.ClickId,
                LabelerId = r.ReferrerId,
                AmountVnd = hoaHong,
                ReleaseAt = h.ReleaseAt,
            });
        }

        public async Task GhiGioiThieuAsync(ReferralRegistered p, CancellationToken ct)
        {
            if (await _db.Referrals.AnyAsync(x => x.ReferredId == p.ReferredId, ct))
            {
                return;
            }

            _db.Referrals.Add(ReferralLink.Tao(p.ReferrerId, p.ReferredId, p.RegisteredAt));
        }

        /// <summary>
        /// link.disabled vi vi pham (WithholdRevenue): moi khoan DANG TREO cua link (doanh thu
        /// sharer va hoa hong gioi thieu) chuyen sang platform:withheld. Khoan da giai phong thi
        /// khong thu hoi — chinh vi vay moi co thoi gian treo.
        /// </summary>
        public async Task GiuDoanhThuLinkAsync(LinkDisabled p, Caller caller, CancellationToken ct)
        {
            if (!p.WithholdRevenue)
            {
                return;
            }

            await _writer.KhoaAsync(ct);

            List<FundsHold> ds = await _db.Holds
                .Where(h => h.LinkId == p.LinkId && h.State == HoldState.Held)
                .ToListAsync(ct);

            DateTimeOffset bayGio = _clock.GetUtcNow();
            long tong = 0;
            foreach (FundsHold h in ds)
            {
                h.GiuLai(bayGio);
                await _writer.GhiAsync(Postings.GiuDoanhThu(h.Id, h.LabelerId, h.AmountVnd, bayGio), ct);
                tong = tong + h.AmountVnd;
            }

            if (ds.Count > 0)
            {
                _logger.LogWarning("Link {LinkId} vi pham: giu lai {So} khoan treo, {Tong}d", p.LinkId, ds.Count, tong);
            }
        }

        private void PhatNganSachCong(ProjectEscrow e, Caller caller)
        {
            _events.Phat(caller, new GateBudgetChanged
            {
                ProjectId = e.ProjectId,
                RemainingVnd = e.NganSachCongConLai(),
                Sequence = e.SoThuTuNganSachMoi(),
            });
        }

        // =====================================================================
        // CHI TRA NHAN DUOC DUYET (VD-M-15)
        // =====================================================================

        /// <summary>
        /// ky quy −(don gia + phi) → labeler TREO +don gia, nen tang +phi; tao khoan
        /// treo; phat funds.held.
        ///
        /// Cac truong hop KHONG DUOC CHI (nem loi → thu lai → DLQ cho nguoi xem,
        /// VD-D-06 — khong bao gio nuot im lang vi day la tien cua nguoi lao dong):
        ///   - du an chua/khong ky quy, hoac ky quy da dong;
        ///   - task da duoc chi du so luot redundancy (VD-M-03);
        ///   - ky quy khong du (LedgerWriter chan bang UPDATE co dieu kien).
        /// </summary>
        public async Task ChiTraNhanAsync(AnnotationApproved a, Caller caller, CancellationToken ct)
        {
            if (a.Source == AnnotationSource.LinkGateway)
            {
                // Nhan tu cong link: nguoi chia se link DA duoc tra theo luot vuot (click.validated);
                // khach vang lai khong co vi. Duyet nhan nay chi la chon loc du lieu, khong chi tien.
                return;
            }

            if (a.LabelerId == null)
            {
                throw new InvalidValueException("thieu_labeler", "Nhan chuyen nghiep thieu labeler — khong chi duoc.");
            }

            await _writer.KhoaAsync(ct);

            if (await _db.Holds.AnyAsync(h => h.AnnotationId == a.AnnotationId, ct))
            {
                _logger.LogInformation("Nhan {AnnotationId} da chi tu truoc — bo qua (VD-M-02)", a.AnnotationId);
                return;
            }

            ProjectEscrow? escrow = await _db.Escrows.FirstOrDefaultAsync(x => x.ProjectId == a.ProjectId, ct);
            if (escrow == null)
            {
                throw new RuleViolationException("chua_ky_quy", "Du an " + a.ProjectId + " chua co ky quy — khong chi duoc.");
            }

            if (escrow.State == EscrowState.Closed)
            {
                throw new RuleViolationException("ky_quy_da_dong", "Ky quy du an " + a.ProjectId + " da tra ve doanh nghiep.");
            }

            int daChi = await _db.Holds.CountAsync(h => h.TaskId == a.TaskId, ct);
            if (escrow.VuotRedundancy(daChi))
            {
                throw new RuleViolationException(
                    "vuot_redundancy",
                    "Task " + a.TaskId + " da chi " + daChi + " luot = redundancy " + escrow.Redundancy + ".");
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            Guid labelerId = a.LabelerId.Value;

            await _writer.GhiAsync(Postings.ChiTraNhan(a.AnnotationId, a.ProjectId, labelerId, a.AmountVnd, a.PlatformFeeVnd, bayGio), ct);

            FundsHold hold = FundsHold.Tao(a.AnnotationId, a.ProjectId, a.TaskId, labelerId, a.AmountVnd, bayGio, _settings.ThoiGian(SettingKeys.LedgerHoldDuration));
            _db.Holds.Add(hold);

            _events.Phat(caller, new FundsHeld
            {
                HoldId = hold.Id,
                AnnotationId = a.AnnotationId,
                LabelerId = labelerId,
                AmountVnd = a.AmountVnd,
                ReleaseAt = hold.ReleaseAt,
            });
        }

        /// <summary>Worker: giai phong MOT khoan treo den han. Goi trong transaction cua worker.</summary>
        public async Task GiaiPhongTreoAsync(FundsHold h, Caller caller, CancellationToken ct)
        {
            DateTimeOffset bayGio = _clock.GetUtcNow();
            h.GiaiPhong(bayGio);

            await _writer.GhiAsync(Postings.GiaiPhongTreo(h.Id, h.LabelerId, h.AmountVnd, bayGio), ct);

            _events.Phat(caller, new HoldExpired { HoldId = h.Id, LabelerId = h.LabelerId, AmountVnd = h.AmountVnd });
        }

        // =====================================================================
        // KET THUC DU AN
        // =====================================================================

        /// <summary>
        /// Huy / hoan thanh: tra TOAN BO so du ky quy con lai ve doanh nghiep. So
        /// du do lay tu CHINH tai khoan escrow:project (VD-M-10) — khong tinh lai tu
        /// don gia x so nhan, nen khong the lech voi thuc te da chi.
        /// </summary>
        public async Task TraKyQuyAsync(Guid projectId, bool laHuy, Caller caller, CancellationToken ct)
        {
            await _writer.KhoaAsync(ct);

            ProjectEscrow? escrow = await _db.Escrows.FirstOrDefaultAsync(x => x.ProjectId == projectId, ct);
            if (escrow == null || escrow.State == EscrowState.Closed)
            {
                // Huy luc con Nhap (chua tung ky quy) hoac event giao lai: khong co gi de tra.
                return;
            }

            long conLai = await _writer.SoDuAsync(AccountCodes.ProjectEscrow(projectId), ct);
            DateTimeOffset bayGio = _clock.GetUtcNow();

            if (conLai > 0)
            {
                await _writer.GhiAsync(Postings.TraKyQuy(projectId, escrow.OwnerId, conLai, laHuy, bayGio), ct);
            }

            escrow.Dong(bayGio);

            if (laHuy)
            {
                _events.Phat(caller, new RefundIssued { ProjectId = projectId, OwnerId = escrow.OwnerId, AmountVnd = conLai });
            }
            else
            {
                _events.Phat(caller, new EscrowReleased { ProjectId = projectId, OwnerId = escrow.OwnerId, AmountVnd = conLai });
            }
        }

        // =====================================================================
        // KET QUA RUT TIEN tu payment-svc
        // =====================================================================

        public async Task HoanTatRutAsync(PayoutCompleted p, CancellationToken ct)
        {
            Withdrawal? w = await _db.Withdrawals.FirstOrDefaultAsync(x => x.Id == p.WithdrawalId, ct);
            if (w == null)
            {
                throw new RuleViolationException("khong_co_lenh_rut", "Khong tim thay lenh rut " + p.WithdrawalId);
            }

            if (w.State != WithdrawalState.Requested)
            {
                return;
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            w.HoanTat(bayGio);
            await _writer.GhiAsync(Postings.HoanTatRut(w.Id, w.NetVnd, p.Provider, bayGio), ct);
        }

        /// <summary>Cong that bai: but toan DAO — tien ve lai labeler, ke ca thue da giu.</summary>
        public async Task ThatBaiRutAsync(PayoutFailed p, CancellationToken ct)
        {
            Withdrawal? w = await _db.Withdrawals.FirstOrDefaultAsync(x => x.Id == p.WithdrawalId, ct);
            if (w == null)
            {
                throw new RuleViolationException("khong_co_lenh_rut", "Khong tim thay lenh rut " + p.WithdrawalId);
            }

            if (w.State != WithdrawalState.Requested)
            {
                return;
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            w.ThatBai(p.Reason, bayGio);
            await _writer.GhiAsync(Postings.DaoRut(w.Id, w.LabelerId, w.NetVnd, w.TaxVnd, bayGio, "Dao but toan rut that bai"), ct);
        }
    }
}
