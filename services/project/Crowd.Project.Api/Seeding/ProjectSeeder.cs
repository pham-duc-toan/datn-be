using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Settings;
using Crowd.BuildingBlocks.Storage;
using Crowd.Labeling;
using Crowd.Project.Api.Helpers;
using Crowd.Project.Domain.Annotations;
using Crowd.Project.Domain.Attempts;
using Crowd.Project.Domain.Collections;
using Crowd.Project.Domain.Datasets;
using Crowd.Project.Domain.Gold;
using Crowd.Project.Domain.Members;
using Crowd.Project.Domain.Projects;
using Crowd.Project.Infrastructure.Persistence;
using Crowd.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crowd.Project.Api.Seeding
{
    /// <summary>
    /// Seed DU AN cua kich ban: du an, thanh vien, dataset + mau (anh PNG / am
    /// thanh WAV len MinIO that; van ban / cap luu noi dung), cau hoi vang, dot thu
    /// bai, bai lam (nhap / da nop), nhan cua bai da nop va lich su bai.
    ///
    /// Truoc khi gop, task-svc va annotation-svc moi ben tu seed ban sao rieng (phat
    /// lai event vao processor) — nay cung database nen seed THANG bang goc.
    ///
    /// Moi buoc di qua PHUONG THUC DOMAIN that (Tao, DatCauHinhGia, YeuCauPublish,
    /// XacNhanDaKyQuy, Duyet...) — kich ban sai luat (vd ngan sach khong du ky
    /// quy) thi seed NEM LOI ngay, khong the tao ra du an o trang thai vo ly.
    ///
    /// Thoi gian "lui ve qua khu" theo kich ban: P1 tao 7 ngay truoc, ky quy 6
    /// ngay truoc, duyet 5 ngay 23 gio truoc...
    /// </summary>
    public sealed class ProjectSeeder
    {
        /// <summary>Anh mau 256 x 256 — du nhin ro tren giao dien, chi vai KB.</summary>
        private const int KichThuocAnh = 256;

        private readonly ProjectDbContext _db;
        private readonly IObjectStorage _storage;
        private readonly TimeProvider _clock;
        private readonly ILogger<ProjectSeeder> _logger;
        private readonly ISettings _settings;

        public ProjectSeeder(ProjectDbContext db, IObjectStorage storage, TimeProvider clock, ILogger<ProjectSeeder> logger, ISettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            _settings = settings;

            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (storage == null)
            {
                throw new ArgumentNullException(nameof(storage));
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
            _storage = storage;
            _clock = clock;
            _logger = logger;
        }

        public async Task ChayAsync(CancellationToken ct)
        {
            // Idempotent THEO TUNG DU AN: DB cu da co P1-P4 thi van them P5-P7 khi
            // kich ban moi bo sung du an.
            List<Guid> ids = KichBanSeed.Projects.Select(p => p.Id).ToList();
            HashSet<Guid> daCo = new HashSet<Guid>(
                await _db.Projects.Where(p => ids.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct));

            DateTimeOffset bayGio = _clock.GetUtcNow();
            int soDuAn = 0;
            int soMau = 0;
            HashSet<string> fileDaDay = new HashSet<string>(StringComparer.Ordinal);

            foreach (SeedProject sp in KichBanSeed.Projects)
            {
                if (daCo.Contains(sp.Id))
                {
                    continue;
                }

                soMau = soMau + await TaoDuAnAsync(sp, bayGio, fileDaDay, ct);
                soDuAn++;
            }

            if (soDuAn == 0)
            {
                _logger.LogInformation("Seed project: da du du an seed — bo qua");
                return;
            }

            // Mot SaveChanges cho ca kich ban: hong o dau thi khong luu gi ca.
            // File da day len MinIO van nam do — khoa tat dinh nen lan seed sau ghi de.
            SeedOutbox.BoEventChuaGui(_db);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Seed project: tao {SoDuAn} du an, {SoMau} mau", soDuAn, soMau);
        }

        private async Task<int> TaoDuAnAsync(SeedProject sp, DateTimeOffset bayGio, HashSet<string> fileDaDay, CancellationToken ct)
        {
            DateTimeOffset lucTao = bayGio - sp.CreatedAgo;

            // ---- 1. Du an Nhap + cau hinh day du ----
            LabelingProject duAn = LabelingProject.Tao(
                sp.OwnerId,
                sp.Name,
                sp.Description,
                sp.Modality,
                sp.IsPrivate ? ProjectVisibility.Private : ProjectVisibility.Public,
                lucTao, QuyDinhTuSetting.DuAn(_settings));
            SeedIds.GanId(duAn, sp.Id);

            LabelSchema tapNhan = sp.TapNhan();
            duAn.DatLabelSchema(tapNhan, lucTao);
            duAn.DatHuongDan(Guideline.Tao(sp.GuidelineMarkdown, null, QuyDinhTuSetting.DuAn(_settings)), lucTao);
            duAn.DatCauHinhGia(sp.RewardVnd, sp.Slots, sp.PassThresholdPercent, sp.Deadline(bayGio), lucTao, QuyDinhTuSetting.DuAn(_settings));
            duAn.DatKenhPhanPhoi(true, false, false, null, null, lucTao);
            duAn.DatDieuKienThamGia(
                null,
                null,
                sp.RequireEntranceTest,
                sp.EntranceQuestionCount,
                sp.EntrancePassPercent,
                lucTao, QuyDinhTuSetting.DuAn(_settings));

            _db.Projects.Add(duAn);

            // ---- 2. Mau (seed tao san o trang thai ready) ----
            foreach (SeedSample s in sp.Samples)
            {
                Sample mau = await TaoMauAsync(sp, s, lucTao, fileDaDay, ct);
                SeedIds.GanId(mau, s.Id);

                // Khoa file do kich ban dat — dung chung giua cac service seed.
                if (s.StorageKey != null)
                {
                    SeedIds.GanThuocTinh(mau, "StorageKey", s.StorageKey);
                }

                _db.Samples.Add(mau);
            }


            // ---- 3. Cau hoi vang — kiem bang tap nhan + metadata nhu API that ----
            foreach (SeedGold g in sp.Gold)
            {
                LabelPayload dapAn = LabelPayload.Tao(tapNhan, g.PayloadJson, sp.Mau(g.SampleId).Metadata);

                GoldItem vang = GoldItem.Tao(
                    sp.Id,
                    g.SampleId,
                    dapAn,
                    g.ForEntranceTest ? GoldPurpose.EntranceTest : GoldPurpose.QualityCheck,
                    lucTao);
                SeedIds.GanId(vang, g.Id);
                _db.GoldItems.Add(vang);
            }

            // ---- 4. Vong doi: publish → ky quy → duyet ----
            if (sp.DaKyQuy)
            {
                DateTimeOffset lucKyQuy = sp.LucKyQuy(bayGio);
                int soCauTest = sp.Gold.Count(g => g.ForEntranceTest);

                duAn.YeuCauPublish(sp.QuestionCount, soCauTest, KichBanSeed.PhanTramPhi, false, QuyDinhTuSetting.DuAn(_settings), null, lucKyQuy);
                duAn.XacNhanDaKyQuy(lucKyQuy);
            }

            DateTimeOffset lucVaoDuAn = lucTao;
            if (sp.DaChay)
            {
                DateTimeOffset lucDuyet = sp.LucDuyet(bayGio);
                duAn.Duyet(lucDuyet);

                // Labeler chi vao duoc du an dang chay.
                lucVaoDuAn = lucDuyet + TimeSpan.FromHours(1);
            }

            if (sp.Stage == SeedStage.Completed)
            {
                // Da cham va qua han khieu nai (docs 3.18).
                duAn.BatDauCham(bayGio - sp.GradedAgo!.Value);
                duAn.HoanThanh(bayGio - sp.CompletedAgo!.Value);
            }

            // ---- 5. Thanh vien ----
            _db.ProjectMembers.Add(ProjectMember.TaoChuSoHuu(sp.Id, sp.OwnerId, lucTao));

            foreach (SeedMember m in sp.Members)
            {
                _db.ProjectMembers.Add(ProjectMember.TaoThanhVien(sp.Id, m.UserId, MemberRole.Labeler, lucVaoDuAn));
            }

            // ---- 6. Dot thu bai + bai lam + nhan ----
            if (sp.DaChay)
            {
                CollectionRound dot = CollectionRound.Mo(
                    sp.Id, sp.Slots, _settings.SoNguyen(SettingKeys.ProjectParticipantMultiplier), sp.LucDuyet(bayGio));
                _db.CollectionRounds.Add(dot);
                TaoBaiLam(sp, dot, sp.Deadline(bayGio), bayGio);
            }

            return sp.Samples.Count;
        }

        /// <summary>
        /// Bai lam — dung CHINH domain (Attempt.BatDau / Nop / GhiKetQua, LabelAnnotation.TaoTuBaiNop)
        /// nhu API that. Du an Completed thi dong thu bai, cham, chot theo dung moc thoi gian cua kich ban. Id bai TAT
        /// DINH (kich ban) de quality-svc / ledger-svc seed khop; nhan khoa theo (du an, bai, mau).
        /// </summary>
        private void TaoBaiLam(SeedProject sp, CollectionRound dot, DateTimeOffset hanChot, DateTimeOffset bayGio)
        {
            if (sp.Attempts.Count == 0)
            {
                return;
            }

            // De = moi mau tru cau vang bai test dau vao (cau vang kiem tra van tron vao bai), theo thu tu kich ban.
            List<Guid> de = new List<Guid>();
            foreach (SeedSample m in sp.Samples)
            {
                bool vangTest = false;
                foreach (SeedGold g in sp.Gold)
                {
                    if (g.SampleId == m.Id && g.ForEntranceTest)
                    {
                        vangTest = true;
                    }
                }

                if (!vangTest)
                {
                    de.Add(m.Id);
                }
            }

            DateTimeOffset? lucCham = sp.GradedAgo.HasValue ? bayGio - sp.GradedAgo.Value : null;

            foreach (SeedAttempt b in sp.Attempts.OrderByDescending(x => x.StartedAgo))
            {
                DateTimeOffset batDau = bayGio - b.StartedAgo;
                DateTimeOffset hetHan = batDau + _settings.ThoiGian(SettingKeys.TaskAttemptDuration);
                if (hanChot < hetHan)
                {
                    hetHan = hanChot;
                }

                Attempt a = Attempt.BatDau(sp.Id, b.LabelerId, de, batDau, hetHan);
                SeedIds.GanId(a, b.AttemptId);
                _db.Attempts.Add(a);

                if (b.State != SeedAttemptState.Submitted)
                {
                    // Dang lam: nhap chi nam o localStorage cua frontend — backend khong luu gi.
                    continue;
                }

                // Da nop: cau that vao annotations, cau vang vao gold_answers — nhu LamBaiService.NopAsync.
                DateTimeOffset lucNop = bayGio - b.SubmittedAgo!.Value;
                JsonObject vang = new JsonObject();
                List<LabelAnnotation> nhan = new List<LabelAnnotation>();
                foreach (SeedAnswer tl in b.Answers)
                {
                    LabelPayload payload = SeedEvents.NhanCua(sp, tl);
                    if (sp.LaCauVang(tl.SampleId))
                    {
                        vang[tl.SampleId.ToString()] = JsonNode.Parse(payload.DataJson);
                        continue;
                    }

                    nhan.Add(LabelAnnotation.TaoTuBaiNop(a.Id, sp.Id, tl.SampleId, b.LabelerId, payload));
                }

                a.Nop(b.GoldTotal, b.GoldCorrect, b.Answers.Count, vang.Count == 0 ? null : vang.ToJsonString(), null, null, lucNop);
                _db.AttemptHistory.Add(AttemptHistoryEntry.Tao(a.Id, sp.Id, AttemptHistoryEntry.DaNop, b.LabelerId, null, lucNop));

                if (b.Result != null)
                {
                    // Trang thai nhan (Accepted / Excluded) suy ra tu ket qua bai — khong ghi tren nhan.
                    a.GhiKetQua(b.Result.ComparableCount, b.Result.MatchedCount, b.Result.AgreementPercent, b.Result.Passed, b.Result.Passed, lucCham!.Value);

                    _db.AttemptHistory.Add(AttemptHistoryEntry.Tao(
                        a.Id, sp.Id, b.Result.Passed ? AttemptHistoryEntry.Dat : AttemptHistoryEntry.Truot, null, null, lucCham.Value));
                }

                _db.Annotations.AddRange(nhan);
            }

            if (sp.Stage == SeedStage.Completed)
            {
                DateTimeOffset lucXong = bayGio - sp.CompletedAgo!.Value;
                dot.DongThuBai("du_suat");
                dot.GhiDaCham(lucCham!.Value, lucXong - lucCham.Value);
                dot.Chot(SeedEvents.SoNguoiDat(sp), lucXong);
            }
        }

        private async Task<Sample> TaoMauAsync(
            SeedProject sp, SeedSample s, DateTimeOffset luc, HashSet<string> fileDaDay, CancellationToken ct)
        {
            switch (s.Modality)
            {
                case Modalities.Image:
                {
                    byte[] png = AnhMauPng.Tao(s.R, s.G, s.B, KichThuocAnh);
                    await _storage.LuuAsync(s.StorageKey!, png, AnhMauPng.ContentType, ct);

                    return Sample.TaoAnhTrongZip(
                        sp.Id, s.FileName, AnhMauPng.ContentType, ".png", png.Length, Bam(png), s.Metadata, luc, QuyDinhTuSetting.DuLieu(_settings));
                }

                case Modalities.Audio:
                {
                    // Cac doan cat tu MOT file dung chung khoa — chi day file mot lan.
                    byte[] wav = AmThanhMau.TaoWav(s.ToneHz, s.FileSeconds);
                    if (fileDaDay.Add(s.StorageKey!))
                    {
                        await _storage.LuuAsync(s.StorageKey!, wav, AmThanhMau.ContentType, ct);
                    }

                    // Dau van tay doan = bam(file + "#" + diem bat dau) — cung cong thuc SampleIngestor.
                    string sha = Bam(wav);
                    if (s.Metadata.SegmentStart.HasValue)
                    {
                        sha = Bam(Encoding.UTF8.GetBytes(sha + "#" + s.Metadata.SegmentStart.Value.ToString("0.###", CultureInfo.InvariantCulture)));
                    }

                    return Sample.TaoTuFile(
                        sp.Id, Modalities.Audio, s.StorageKey!, s.FileName, AmThanhMau.ContentType, wav.Length, sha, s.Metadata, luc, QuyDinhTuSetting.DuLieu(_settings));
                }

                default:
                {
                    string noiDung = s.ContentJson!;
                    return Sample.TaoTuNoiDung(
                        sp.Id, s.Modality, RawJson.Tu(noiDung), s.FileName, Bam(Encoding.UTF8.GetBytes(noiDung)), s.Metadata, luc, QuyDinhTuSetting.DuLieu(_settings));
                }
            }
        }

        private static string Bam(byte[] b)
        {
            return Convert.ToHexStringLower(SHA256.HashData(b));
        }
    }
}
