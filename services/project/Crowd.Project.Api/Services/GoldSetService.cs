using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Settings;
using Crowd.Contracts.Project;
using Crowd.Labeling;
using Crowd.Project.Api.Dtos;
using Crowd.Project.Api.Exceptions;
using Crowd.Project.Api.Helpers;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.Gold;
using Crowd.Project.Domain.Projects;
using Crowd.Project.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Project.Api.Services
{
    /// <summary>
    /// Cau hoi vang (FB-16). Moi thay doi phat gold_set.updated mang TOAN BO tap
    /// sau thay doi — consumer chi viec thay the ban sao, khong phai cong don.
    ///
    /// Dap an vang la BI MAT cua du an: chi chu du an xem duoc.
    /// </summary>
    public sealed class GoldSetService
    {
        private readonly ProjectDbContext _db;
        private readonly ProjectAccessService _access;
        private readonly ProjectEventPublisher _events;
        private readonly TimeProvider _clock;
        private readonly ISettings _settings;

        public GoldSetService(
            ProjectDbContext db,
            ProjectAccessService access,
            ProjectEventPublisher events,
            TimeProvider clock,
            ISettings settings)
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

            if (access == null)
            {
                throw new ArgumentNullException(nameof(access));
            }

            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _db = db;
            _access = access;
            _events = events;
            _clock = clock;
        }

        public async Task<IReadOnlyList<GoldItemResponse>> DanhSachAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            await _access.LayDeQuanLyAsync(projectId, caller, ct);

            List<GoldItem> ds = await _db.GoldItems
                .AsNoTracking()
                .Where(g => g.ProjectId == projectId)
                .OrderBy(g => g.CreatedAt)
                .ToListAsync(ct);

            List<GoldItemResponse> ketQua = new List<GoldItemResponse>();
            foreach (GoldItem g in ds)
            {
                ketQua.Add(TaoResponse(g));
            }

            return ketQua;
        }

        public async Task<IReadOnlyList<GoldItemResponse>> ThemAsync(
            Guid projectId, AddGoldItemsRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null || body.Items == null || body.Items.Count == 0)
            {
                throw new InvalidValueException("khong_co_cau_hoi", "Can it nhat mot cau hoi vang.");
            }

            LabelingProject duAn = await _access.LayDeQuanLyAsync(projectId, caller, ct);
            duAn.KiemTraCoTheSuaCauHoiVang();
            LabelSchema schema = duAn.LabelSchema!;

            List<GoldItem> hienCo = await _db.GoldItems.Where(g => g.ProjectId == projectId).ToListAsync(ct);
            if (hienCo.Count + body.Items.Count > _settings.SoNguyen(SettingKeys.ProjectGoldItemsMax))
            {
                throw new InvalidValueException("qua_nhieu_cau_vang", "Toi da " + _settings.SoNguyen(SettingKeys.ProjectGoldItemsMax) + " cau hoi vang moi du an.");
            }

            // Moi mau phai thuoc CHINH du an nay (chong BOLA) va chua lam cau vang.
            List<Guid> mauYeuCau = new List<Guid>();
            foreach (GoldItemInput i in body.Items)
            {
                if (i.SampleId == null || i.Purpose == null || i.ExpectedPayload == null)
                {
                    throw new InvalidValueException("thieu_thong_tin", "Moi cau hoi vang can sampleId, expectedPayload va purpose.");
                }

                mauYeuCau.Add(i.SampleId.Value);
            }

            if (mauYeuCau.Distinct().Count() != mauYeuCau.Count)
            {
                throw new InvalidValueException("mau_trung", "Mot mau xuat hien hai lan trong yeu cau.");
            }

            // Metadata mau (rong x cao, thoi luong...) de kiem dap an nam trong mau.
            Dictionary<Guid, RawJson> metadataMau = await _db.Samples
                .AsNoTracking()
                .Where(s => s.ProjectId == projectId && mauYeuCau.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Metadata, ct);
            if (metadataMau.Count != mauYeuCau.Count)
            {
                throw new InvalidValueException("mau_khong_thuoc_du_an", "Co mau khong thuoc du an nay.");
            }

            HashSet<Guid> daLaVang = new HashSet<Guid>(hienCo.Select(g => g.SampleId));
            DateTimeOffset bayGio = _clock.GetUtcNow();
            List<GoldItem> moi = new List<GoldItem>();

            foreach (GoldItemInput i in body.Items)
            {
                if (daLaVang.Contains(i.SampleId!.Value))
                {
                    throw new RuleViolationException("da_la_cau_vang", "Mau " + i.SampleId + " da la cau hoi vang.");
                }

                // Loai du lieu va cong cu do TAP NHAN cua du an quyet dinh, client chi
                // gui phan du lieu. Sai dinh dang → LabelFormatException → 400.
                LabelPayload dapAn = LabelPayload.Tao(
                    schema,
                    i.ExpectedPayload!.Value,
                    SampleMetadata.Tu(metadataMau[i.SampleId.Value]));

                GoldItem g = GoldItem.Tao(
                    projectId,
                    i.SampleId.Value,
                    dapAn,
                    i.Purpose!.Value,
                    bayGio);

                moi.Add(g);
            }

            _db.GoldItems.AddRange(moi);

            List<GoldItem> tapSau = new List<GoldItem>(hienCo);
            tapSau.AddRange(moi);
            PhatTapMoi(projectId, tapSau, caller);

            await _db.SaveChangesAsync(ct);

            List<GoldItemResponse> ketQua = new List<GoldItemResponse>();
            foreach (GoldItem g in moi)
            {
                ketQua.Add(TaoResponse(g));
            }

            return ketQua;
        }

        public async Task XoaAsync(Guid projectId, Guid goldItemId, Caller caller, CancellationToken ct)
        {
            LabelingProject duAn = await _access.LayDeQuanLyAsync(projectId, caller, ct);
            duAn.KiemTraCoTheSuaCauHoiVang();

            List<GoldItem> hienCo = await _db.GoldItems.Where(g => g.ProjectId == projectId).ToListAsync(ct);
            GoldItem? canXoa = hienCo.FirstOrDefault(g => g.Id == goldItemId);

            if (canXoa == null)
            {
                throw new NotFoundException("Khong tim thay cau hoi vang.");
            }

            _db.GoldItems.Remove(canXoa);
            hienCo.Remove(canXoa);
            PhatTapMoi(projectId, hienCo, caller);

            await _db.SaveChangesAsync(ct);
        }

        private void PhatTapMoi(Guid projectId, List<GoldItem> tap, Caller caller)
        {
            List<GoldSetItem> items = new List<GoldSetItem>();
            foreach (GoldItem g in tap)
            {
                items.Add(new GoldSetItem
                {
                    SampleId = g.SampleId,
                    ExpectedPayload = g.ExpectedPayload,
                    Purpose = ContractMapper.ToContract(g.Purpose),
                });
            }

            _events.Phat(caller, new GoldSetUpdated { ProjectId = projectId, Items = items });
        }

        private static GoldItemResponse TaoResponse(GoldItem g)
        {
            return new GoldItemResponse
            {
                Id = g.Id,
                SampleId = g.SampleId,
                ExpectedPayload = g.ExpectedPayload,
                Purpose = g.Purpose,
            };
        }
    }
}
