using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Storage;
using Crowd.Labeling;
using Crowd.Project.Api.Dtos;
using Crowd.Project.Api.Exceptions;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.EntranceTests;
using Crowd.Project.Domain.Gold;
using Crowd.Project.Domain.Members;
using Crowd.Project.Domain.Projects;
using Crowd.Project.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Project.Api.Services
{
    /// <summary>
    /// Bai test dau vao (FL-03): labeler lam dat thi thanh thanh vien du an.
    ///
    /// Cau hoi rut NGAU NHIEN tu cau vang muc dich EntranceTest, moi lan mot bo
    /// khac — lam lai khong gap dung bo cu. Dap an KHONG BAO GIO roi server.
    /// </summary>
    public sealed class EntranceTestService
    {
        private readonly ProjectDbContext _db;
        private readonly ProjectAccessService _access;
        private readonly MemberService _members;
        private readonly IObjectStorage _storage;
        private readonly TimeProvider _clock;

        public EntranceTestService(
            ProjectDbContext db,
            ProjectAccessService access,
            MemberService members,
            IObjectStorage storage,
            TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (access == null)
            {
                throw new ArgumentNullException(nameof(access));
            }

            if (members == null)
            {
                throw new ArgumentNullException(nameof(members));
            }

            if (storage == null)
            {
                throw new ArgumentNullException(nameof(storage));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _db = db;
            _access = access;
            _members = members;
            _storage = storage;
            _clock = clock;
        }

        public async Task<EntranceTestStartResponse> BatDauAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            LabelingProject duAn = await _access.LayDeXemAsync(projectId, caller, ct);
            duAn.KiemTraChoTuThamGia();

            if (!duAn.RequireEntranceTest)
            {
                throw new RuleViolationException("khong_can_test", "Du an khong yeu cau bai test — tham gia truc tiep.");
            }

            Guid userId = caller.LayUserId();
            DateTimeOffset bayGio = _clock.GetUtcNow();

            bool daLaThanhVien = await _db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.UserId == userId, ct);
            if (daLaThanhVien)
            {
                throw new RuleViolationException("da_la_thanh_vien", "Ban da la thanh vien (hoac da bi chan khoi) du an nay.");
            }

            List<EntranceAttempt> cacLan = await _db.EntranceAttempts
                .Where(a => a.ProjectId == projectId && a.UserId == userId)
                .ToListAsync(ct);

            bool dangMo = cacLan.Any(a => a.DangMo(bayGio));

            List<Guid> kho = await _db.GoldItems
                .Where(g => g.ProjectId == projectId && g.Purpose == GoldPurpose.EntranceTest)
                .Select(g => g.SampleId)
                .ToListAsync(ct);

            List<Guid> cauHoi = RutNgauNhien(kho, duAn.EntranceQuestionCount);

            EntranceAttempt lanMoi = EntranceAttempt.BatDau(projectId, userId, cauHoi, cacLan.Count, dangMo, bayGio);
            _db.EntranceAttempts.Add(lanMoi);
            await _db.SaveChangesAsync(ct);

            Dictionary<Guid, string> khoa = await _db.Samples
                .Where(s => cauHoi.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.StorageKey, ct);

            List<EntranceQuestion> ds = new List<EntranceQuestion>();
            foreach (Guid id in lanMoi.QuestionSampleIds)
            {
                ds.Add(new EntranceQuestion
                {
                    SampleId = id,
                    ImageUrl = await _storage.TaoLinkXemAsync(khoa[id]),
                });
            }

            return new EntranceTestStartResponse
            {
                AttemptId = lanMoi.Id,
                ExpiresAt = lanMoi.ExpiresAt,
                TaskType = duAn.LabelTaskType,
                SchemaVersion = LabelFormats.PhienBanMoiNhat(duAn.LabelTaskType),
                LabelClasses = duAn.LabelSchema!.Classes,
                AllowMultiple = duAn.LabelSchema.AllowMultiple,
                Questions = ds,
            };
        }

        public async Task<EntranceResultResponse> NopAsync(
            Guid projectId, Guid attemptId, SubmitEntranceTestRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            Guid userId = caller.LayUserId();

            // Chi lay lan lam bai CUA CHINH nguoi goi. Lan cua nguoi khac → 404,
            // khong tiet lo attemptId do co ton tai.
            EntranceAttempt? lan = await _db.EntranceAttempts
                .FirstOrDefaultAsync(a => a.Id == attemptId && a.ProjectId == projectId && a.UserId == userId, ct);

            if (lan == null)
            {
                throw new NotFoundException("Khong tim thay bai test.");
            }

            LabelingProject duAn = await _access.LayDeXemAsync(projectId, caller, ct);

            List<Guid> cauHoi = new List<Guid>(lan.QuestionSampleIds);
            List<GoldItem> vang = await _db.GoldItems
                .AsNoTracking()
                .Where(g => g.ProjectId == projectId && cauHoi.Contains(g.SampleId))
                .ToListAsync(ct);

            Dictionary<Guid, LabelPayload> dapAn = new Dictionary<Guid, LabelPayload>();
            foreach (GoldItem g in vang)
            {
                dapAn[g.SampleId] = g.ExpectedPayload;
            }

            // Cau tra loi doc theo loai nhan cua DU AN, phien ban moi nhat. Sai
            // dinh dang → LabelFormatException → 400, bai test chua bi tinh la da nop.
            string loaiNhan = duAn.LabelTaskType;
            int phienBan = LabelFormats.PhienBanMoiNhat(loaiNhan);

            Dictionary<Guid, LabelPayload> traLoi = new Dictionary<Guid, LabelPayload>();
            if (body.Answers != null)
            {
                foreach (EntranceAnswer a in body.Answers)
                {
                    if (a.SampleId.HasValue && a.Payload.HasValue)
                    {
                        traLoi[a.SampleId.Value] = LabelPayload.Tao(loaiNhan, phienBan, a.Payload.Value);
                    }
                }
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            bool dau = lan.Nop(traLoi, dapAn, duAn.EntrancePassPercent, bayGio);

            int soLanDaLam = await _db.EntranceAttempts.CountAsync(a => a.ProjectId == projectId && a.UserId == userId, ct);
            bool vuaThamGia = false;

            if (dau)
            {
                // Luu ket qua bai test VA them thanh vien trong CUNG SaveChanges
                // (LuuThanhVienMoiAsync goi SaveChanges cho ca hai).
                ProjectMember moi = ProjectMember.TaoThanhVien(projectId, userId, MemberRole.Labeler, bayGio);
                await _members.LuuThanhVienMoiAsync(moi, caller, ct);
                vuaThamGia = true;
            }
            else
            {
                await _db.SaveChangesAsync(ct);
            }

            return new EntranceResultResponse
            {
                AttemptId = lan.Id,
                ScorePercent = lan.ScorePercent ?? 0,
                PassPercent = duAn.EntrancePassPercent,
                Passed = dau,
                JoinedProject = vuaThamGia,
                AttemptsLeft = Math.Max(0, EntranceAttempt.SoLanToiDa - soLanDaLam),
            };
        }

        public async Task<IReadOnlyList<EntranceAttemptResponse>> LichSuAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            Guid userId = caller.LayUserId();

            List<EntranceAttempt> ds = await _db.EntranceAttempts
                .AsNoTracking()
                .Where(a => a.ProjectId == projectId && a.UserId == userId)
                .OrderBy(a => a.StartedAt)
                .ToListAsync(ct);

            List<EntranceAttemptResponse> ketQua = new List<EntranceAttemptResponse>();
            foreach (EntranceAttempt a in ds)
            {
                ketQua.Add(new EntranceAttemptResponse
                {
                    AttemptId = a.Id,
                    StartedAt = a.StartedAt,
                    ExpiresAt = a.ExpiresAt,
                    SubmittedAt = a.SubmittedAt,
                    ScorePercent = a.ScorePercent,
                    Passed = a.Passed,
                });
            }

            return ketQua;
        }

        /// <summary>
        /// Rut n phan tu ngau nhien (Fisher–Yates). Dung RandomNumberGenerator chu
        /// khong dung Random: bo cau hoi khong duoc doan truoc.
        /// </summary>
        private static List<Guid> RutNgauNhien(List<Guid> kho, int n)
        {
            List<Guid> ban = new List<Guid>(kho);

            for (int i = ban.Count - 1; i > 0; i--)
            {
                int j = RandomNumberGenerator.GetInt32(i + 1);
                Guid tam = ban[i];
                ban[i] = ban[j];
                ban[j] = tam;
            }

            return ban.Take(Math.Min(n, ban.Count)).ToList();
        }
    }
}
