using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Settings;
using Crowd.Contracts.Gate;
using Crowd.Contracts.Ledger;
using Crowd.Contracts.Link;
using Crowd.Contracts.Payment;
using Crowd.Contracts.Project;
using Crowd.Contracts.Quality;
using Crowd.Contracts.Tasking;
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
            ProjectEscrow kyQuy = ProjectEscrow.Tao(p.ProjectId, p.OwnerId, p.EscrowAmountVnd, bayGio);
            kyQuy.DatTen(p.ProjectName);
            _db.Escrows.Add(kyQuy);

            _events.Phat(caller, new EscrowReserved { ProjectId = p.ProjectId, AmountVnd = p.EscrowAmountVnd });
        }

        /// <summary>
        /// project.published: chot so suat, thuong, phi (chan tra vuot ky quy, VD-M-03) va ngan sach cong
        /// link. Du an bat cong link thi bao gate-svc ngan sach ban dau.
        /// </summary>
        public async Task GhiDieuKhoanAsync(ProjectPublished p, Caller caller, CancellationToken ct)
        {
            ProjectEscrow? e = await _db.Escrows.FirstOrDefaultAsync(x => x.ProjectId == p.ProjectId, ct);
            if (e != null)
            {
                e.GhiDieuKhoan(p.Slots, p.RewardVnd, p.PlatformFeeVnd, p.AllowLinkGateway, p.GateBudgetVnd ?? 0);

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
            if (_settings.Chuoi(SettingKeys.LedgerGatePayoutMode) == "batched")
            {
                await XepHangCongLinkAsync(c, ct);
                return;
            }

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

            await ChiHoaHongAsync(c.SharerId, c.SharerAmountVnd, c.PlatformAmountVnd, c.ClickId, c.ProjectId, c.LinkId, treo, bayGio, caller, ct);
            PhatNganSachCong(escrow, caller);
        }

        /// <summary>
        /// Che do batched: chi GHI luot vao hang doi — khong khoa so cai, khong but toan. Day la
        /// toan bo viec cua duong nong; GateBatchWorker lo phan con lai.
        /// </summary>
        private async Task XepHangCongLinkAsync(ClickValidated c, CancellationToken ct)
        {
            if (await _db.GateClicks.AnyAsync(x => x.ClickId == c.ClickId, ct)
                || await _db.Holds.AnyAsync(h => h.AnnotationId == c.ClickId && h.Kind == HoldKind.GateClick, ct))
            {
                return;
            }

            _db.GateClicks.Add(GateClick.Tao(
                c.ClickId, c.LinkId, c.SharerId, c.ProjectId, c.SharerAmountVnd, c.PlatformAmountVnd, c.ValidatedAt, _clock.GetUtcNow()));
        }

        /// <summary>
        /// Gop MOT lo luot cong link dang cho (VD-M-08). Moi du an: nhan luot theo thu tu toi het
        /// ngan sach cong link, MOT but toan cho ca lo, MOT khoan treo cho moi (sharer, link).
        /// Goi trong transaction cua worker. Tra ve so luot da xu ly.
        /// </summary>
        public async Task<int> ChiTheoLoAsync(int toiDa, Caller caller, CancellationToken ct)
        {
            await _writer.KhoaAsync(ct);

            List<GateClick> lo = await _db.GateClicks
                .Where(x => x.State == GateClickState.Queued)
                .OrderBy(x => x.ReceivedAt)
                .Take(toiDa)
                .ToListAsync(ct);
            if (lo.Count == 0)
            {
                return 0;
            }

            Guid batchId = Guid.CreateVersion7();
            DateTimeOffset bayGio = _clock.GetUtcNow();
            TimeSpan treo = _settings.ThoiGian(SettingKeys.LedgerHoldDuration);

            foreach (IGrouping<Guid, GateClick> theoDuAn in lo.GroupBy(x => x.ProjectId))
            {
                ProjectEscrow? escrow = await _db.Escrows.FirstOrDefaultAsync(x => x.ProjectId == theoDuAn.Key, ct);
                long conLai = escrow == null ? 0 : escrow.NganSachCongConLai();

                Dictionary<Guid, long> theoSharer = new Dictionary<Guid, long>();
                Dictionary<(Guid, Guid), long[]> theoSharerLink = new Dictionary<(Guid, Guid), long[]>();
                long nenTang = 0;
                long tong = 0;

                foreach (GateClick c in theoDuAn)
                {
                    if (c.TongVnd > conLai - tong)
                    {
                        c.TuChoi(batchId, bayGio);
                        continue;
                    }

                    tong += c.TongVnd;
                    nenTang += c.PlatformAmountVnd;
                    long cu;
                    theoSharer[c.SharerId] = (theoSharer.TryGetValue(c.SharerId, out cu) ? cu : 0) + c.SharerAmountVnd;
                    long[]? sl;
                    if (!theoSharerLink.TryGetValue((c.SharerId, c.LinkId), out sl))
                    {
                        sl = new long[2];
                        theoSharerLink[(c.SharerId, c.LinkId)] = sl;
                    }

                    sl[0] += c.SharerAmountVnd;
                    sl[1] += c.PlatformAmountVnd;
                    c.DaChi(batchId, bayGio);
                }

                if (theoSharer.Count == 0 || escrow == null)
                {
                    if (escrow != null)
                    {
                        PhatNganSachCong(escrow, caller);
                    }

                    continue;
                }

                await _writer.GhiAsync(Postings.ChiTraCongLinkLo(batchId, theoDuAn.Key, theoSharer, nenTang, bayGio), ct);
                escrow.TieuChoCongLink(tong);

                foreach (KeyValuePair<(Guid, Guid), long[]> sl in theoSharerLink)
                {
                    Guid khoaTreo = Guid.CreateVersion7();
                    FundsHold h = FundsHold.TaoChoCongLink(HoldKind.GateClick, khoaTreo, theoDuAn.Key, sl.Key.Item2, sl.Key.Item1, sl.Value[0], bayGio, treo);
                    _db.Holds.Add(h);
                    _events.Phat(caller, new FundsHeld
                    {
                        HoldId = h.Id,
                        AnnotationId = khoaTreo,
                        LabelerId = sl.Key.Item1,
                        AmountVnd = sl.Value[0],
                        ReleaseAt = h.ReleaseAt,
                    });
                    await ChiHoaHongAsync(sl.Key.Item1, sl.Value[0], sl.Value[1], khoaTreo, theoDuAn.Key, sl.Key.Item2, treo, bayGio, caller, ct);
                }

                PhatNganSachCong(escrow, caller);
            }

            return lo.Count;
        }

        /// <summary>
        /// FS-08: nguoi gioi thieu huong link.referral_percent doanh thu cong link cua nguoi duoc
        /// moi — LAY TU PHAN NEN TANG, khong tru vao nguoi duoc moi. Chi tra khi nguoi duoc moi
        /// da tu kiem >= link.referral_min_earnings_vnd (VD-L-05: clone tu moi nhau khong co gi).
        /// </summary>
        private async Task ChiHoaHongAsync(
            Guid sharerId, long sharerAmount, long platformAmount, Guid nguon, Guid projectId, Guid linkId,
            TimeSpan treo, DateTimeOffset bayGio, Caller caller, CancellationToken ct)
        {
            ReferralLink? r = await _db.Referrals.AsNoTracking().FirstOrDefaultAsync(x => x.ReferredId == sharerId, ct);
            int phanTram = _settings.SoNguyen(SettingKeys.LinkReferralPercent);
            if (r == null || phanTram <= 0)
            {
                return;
            }

            // Tong da kiem qua cong link (khoan dang treo + da giai phong, KHONG tinh khoan bi giu
            // lai vi vi pham), CHUA gom luot nay (chua SaveChanges).
            long daKiem = await _db.Holds
                .Where(h => h.LabelerId == sharerId && h.Kind == HoldKind.GateClick && h.State != HoldState.Withheld)
                .SumAsync(h => h.AmountVnd, ct);
            if (daKiem + sharerAmount < _settings.SoLon(SettingKeys.LinkReferralMinEarningsVnd))
            {
                return;
            }

            long hoaHong = Math.Min(sharerAmount * phanTram / 100, platformAmount);
            if (hoaHong <= 0)
            {
                return;
            }

            await _writer.GhiAsync(Postings.HoaHongGioiThieu(nguon, r.ReferrerId, hoaHong, bayGio), ct);
            FundsHold h = FundsHold.TaoChoCongLink(HoldKind.Referral, nguon, projectId, linkId, r.ReferrerId, hoaHong, bayGio, treo);
            _db.Holds.Add(h);
            _events.Phat(caller, new FundsHeld
            {
                HoldId = h.Id,
                AnnotationId = nguon,
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
        // THUONG BAI DAT (docs 3.18, VD-M-15)
        // =====================================================================

        /// <summary>
        /// project.graded: moi bai DAT nhan thuong cua mot suat — ky quy −(thuong + phi) → labeler +thuong
        /// (treo ledger.reward_hold), nen tang +phi; phat funds.held. Bai truot khong nhan gi.
        ///
        /// Cac truong hop KHONG DUOC CHI (nem loi → thu lai → DLQ cho nguoi xem, VD-D-06 — khong bao gio
        /// nuot im lang vi day la tien cua nguoi lao dong):
        ///   - du an chua/khong ky quy, chua co dieu khoan (project.published), hoac ky quy da dong;
        ///   - da tra du so suat (VD-M-03);
        ///   - ky quy khong du (LedgerWriter chan bang UPDATE co dieu kien).
        /// </summary>
        public async Task ChiThuongDuAnAsync(ProjectGraded p, Caller caller, CancellationToken ct)
        {
            await _writer.KhoaAsync(ct);
            ProjectEscrow escrow = await KyQuyDeTraAsync(p.ProjectId, ct);

            int daTra = await DemBaiDaTraAsync(p.ProjectId, ct);
            int moi = 0;
            foreach (GradedAttempt a in p.Attempts.Where(x => x.Passed).OrderBy(x => x.AttemptId))
            {
                if (await ChiMotBaiAsync(escrow, a.AttemptId, a.LabelerId, daTra + moi, caller, ct))
                {
                    moi++;
                }
            }

            _logger.LogInformation("Du an {ProjectId}: tra thuong {Moi} bai dat (tong {Tong})", p.ProjectId, moi, daTra + moi);
            await DongNeuDaTraDuAsync(escrow, daTra + moi, caller, ct);
        }

        /// <summary>attempt.appeal_resolved: khieu nai ca bai duoc chap nhan → bai thanh dat, tra thuong.</summary>
        public async Task ChiThuongKhieuNaiAsync(AttemptAppealResolved p, Caller caller, CancellationToken ct)
        {
            if (!p.Accepted)
            {
                return;
            }

            await _writer.KhoaAsync(ct);
            ProjectEscrow escrow = await KyQuyDeTraAsync(p.ProjectId, ct);

            int daTra = await DemBaiDaTraAsync(p.ProjectId, ct);
            bool moi = await ChiMotBaiAsync(escrow, p.AttemptId, p.LabelerId, daTra, caller, ct);
            await DongNeuDaTraDuAsync(escrow, moi ? daTra + 1 : daTra, caller, ct);
        }

        private async Task<ProjectEscrow> KyQuyDeTraAsync(Guid projectId, CancellationToken ct)
        {
            ProjectEscrow? escrow = await _db.Escrows.FirstOrDefaultAsync(x => x.ProjectId == projectId, ct);
            if (escrow == null)
            {
                throw new RuleViolationException("chua_ky_quy", "Du an " + projectId + " chua co ky quy — khong tra thuong duoc.");
            }

            if (!escrow.DaCoDieuKhoan)
            {
                // project.published chua toi (queue khac): thu lai sau.
                throw new RuleViolationException("chua_co_dieu_khoan", "Ky quy du an " + projectId + " chua co dieu khoan thuong.");
            }

            if (escrow.State == EscrowState.Closed)
            {
                throw new RuleViolationException("ky_quy_da_dong", "Ky quy du an " + projectId + " da tra ve doanh nghiep.");
            }

            return escrow;
        }

        /// <summary>Tra thuong MOT bai. false = bai da duoc tra tu truoc (event giao lai, VD-M-02).</summary>
        private async Task<bool> ChiMotBaiAsync(ProjectEscrow escrow, Guid attemptId, Guid labelerId, int daTra, Caller caller, CancellationToken ct)
        {
            if (await _db.Holds.AnyAsync(h => h.AnnotationId == attemptId && h.Kind == HoldKind.ProjectReward, ct))
            {
                _logger.LogInformation("Bai {AttemptId} da tra thuong tu truoc — bo qua (VD-M-02)", attemptId);
                return false;
            }

            if (escrow.VuotSoSuat(daTra))
            {
                throw new RuleViolationException(
                    "vuot_so_suat", "Du an " + escrow.ProjectId + " da tra " + daTra + " bai = so suat " + escrow.Slots + ".");
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            await _writer.GhiAsync(Postings.ChiThuongBaiDat(attemptId, escrow.ProjectId, labelerId, escrow.RewardVnd, escrow.PlatformFeeVnd, bayGio), ct);

            TimeSpan treo = _settings.ThoiGian(SettingKeys.LedgerRewardHold);
            FundsHold hold = FundsHold.TaoChoBaiDat(attemptId, escrow.ProjectId, labelerId, escrow.RewardVnd, bayGio, treo);
            _db.Holds.Add(hold);

            _events.Phat(caller, new FundsHeld
            {
                HoldId = hold.Id,
                AnnotationId = attemptId,
                LabelerId = labelerId,
                AmountVnd = escrow.RewardVnd,
                ReleaseAt = hold.ReleaseAt,
            });

            // ledger.reward_hold = 0 (mac dinh): thuong vao so du kha dung NGAY.
            if (treo <= TimeSpan.Zero)
            {
                await GiaiPhongTreoAsync(hold, caller, ct);
            }

            return true;
        }

        private async Task DongNeuDaTraDuAsync(ProjectEscrow escrow, int soBaiDaTra, Caller caller, CancellationToken ct)
        {
            // Du an da ket thuc, ky quy dang cho tra not bai dat: tra du thi hoan phan con lai.
            if (escrow.DaChiDuDeDong(soBaiDaTra))
            {
                await DongVaHoanKyQuyAsync(escrow, escrow.ClosingIsCancel, caller, ct);
            }
        }

        /// <summary>So bai cua du an da duoc tra thuong — moi bai mot khoan treo (SaveChanges chua chay: cong them so vua tao).</summary>
        private Task<int> DemBaiDaTraAsync(Guid projectId, CancellationToken ct)
        {
            return _db.Holds.CountAsync(h => h.ProjectId == projectId && h.Kind == HoldKind.ProjectReward, ct);
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
        /// Huy / hoan thanh: tra TOAN BO so du ky quy con lai ve doanh nghiep. So du do lay tu CHINH tai
        /// khoan escrow:project (VD-M-10) — khong tinh lai tu thuong x so bai, nen khong the lech voi thuc
        /// te da chi.
        ///
        /// NHUNG chi khi da tra DU soBaiDat (project.completed mang so bai dat cuoi cung, ke ca khieu nai
        /// duoc chap nhan): project.graded / attempt.appeal_resolved va project.completed di cac QUEUE khac
        /// nhau, co the toi SAU. Hoan ngay thi tra thuong toi sau gap ky_quy_da_dong → DLQ, labeler dat ma
        /// khong bao gio duoc tra (TLA+ NC-B-01). Chua du → Closing: van tra cho bai dat, tra du thi hoan
        /// phan con lai. Huy du an chi duoc khi chua co bai nop nao → soBaiDat = 0.
        /// </summary>
        public async Task TraKyQuyAsync(Guid projectId, bool laHuy, int soBaiDat, Caller caller, CancellationToken ct)
        {
            await _writer.KhoaAsync(ct);

            ProjectEscrow? escrow = await _db.Escrows.FirstOrDefaultAsync(x => x.ProjectId == projectId, ct);
            if (escrow == null || escrow.State != EscrowState.Active)
            {
                // Huy luc con Nhap (chua tung ky quy), dang dong / da dong, hoac event giao lai.
                return;
            }

            int daTra = await DemBaiDaTraAsync(projectId, ct);
            if (daTra < soBaiDat)
            {
                escrow.BatDauDong(soBaiDat, laHuy);
                _logger.LogInformation(
                    "Du an {ProjectId} ket thuc nhung moi tra {DaTra}/{CanTra} bai dat — giu ky quy cho toi khi tra du",
                    projectId, daTra, soBaiDat);
                return;
            }

            await DongVaHoanKyQuyAsync(escrow, laHuy, caller, ct);
        }

        private async Task DongVaHoanKyQuyAsync(ProjectEscrow escrow, bool laHuy, Caller caller, CancellationToken ct)
        {
            Guid projectId = escrow.ProjectId;
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
