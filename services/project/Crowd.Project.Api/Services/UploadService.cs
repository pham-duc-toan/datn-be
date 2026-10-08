using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Settings;
using Crowd.BuildingBlocks.Storage;
using Crowd.Labeling;
using Crowd.Project.Api.Dtos;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.Projects;
using Microsoft.Extensions.Options;

namespace Crowd.Project.Api.Services
{
    /// <summary>
    /// Cap LINK UPLOAD co han de chu du an tai file THANG len MinIO (khong di qua
    /// service — video vai GB khong lam nghen project-svc):
    ///
    ///   1. POST /projects/{id}/uploads  {"files":[{"name":"a.wav","sizeBytes":...}]}
    ///      → moi file mot { key, uploadUrl }.
    ///   2. Client: PUT file len uploadUrl.
    ///   3. POST /projects/{id}/datasets/manifest  {"rows":[{"file":"&lt;key&gt;"}...]}
    ///
    /// KHOA do HE THONG dat: "{projectId}/raw/{guid}{duoi}" — khong dung ten file
    /// nguoi dung (co the chua "../"), va nam duoi thu muc du an nen manifest cua
    /// du an khac khong tro toi duoc (Sample.TaoTuFile kiem tien to).
    ///
    /// Duoi file chi la loc so bo; LOAI THAT duoc DatasetIngestor kiem lai bang
    /// noi dung file (magic bytes / ffprobe).
    /// </summary>
    public sealed class UploadService
    {

        private static readonly Dictionary<string, string[]> DuoiChoPhep = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            { Modalities.Image, new string[] { ".jpg", ".jpeg", ".png", ".webp" } },
            { Modalities.Audio, new string[] { ".wav", ".mp3", ".m4a", ".ogg", ".flac", ".aac" } },
            { Modalities.Video, new string[] { ".mp4", ".mov", ".webm", ".mkv" } },
            { Modalities.Text, Array.Empty<string>() },
            { Modalities.Pair, Array.Empty<string>() },
        };

        /// <summary>File manifest (.jsonl / .json) duoc upload cho moi loai du lieu.</summary>
        private static readonly string[] DuoiManifest = new string[] { ".jsonl", ".json" };

        private readonly ProjectAccessService _access;
        private readonly IObjectStorage _storage;
        private readonly TimeProvider _clock;
        private readonly ISettings _settings;

        public UploadService(
            ProjectAccessService access,
            IObjectStorage storage,
            TimeProvider clock,
            ISettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            _settings = settings;

            if (access == null)
            {
                throw new ArgumentNullException(nameof(access));
            }

            if (storage == null)
            {
                throw new ArgumentNullException(nameof(storage));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _access = access;
            _storage = storage;
            _clock = clock;
        }

        public async Task<IReadOnlyList<UploadSlotResponse>> TaoLinkAsync(
            Guid projectId, CreateUploadsRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null || body.Files == null || body.Files.Count == 0)
            {
                throw new InvalidValueException("khong_co_file", "Can it nhat mot file trong \"files\".");
            }

            if (body.Files.Count > _settings.SoNguyen(SettingKeys.UploadMaxFiles))
            {
                throw new InvalidValueException("qua_nhieu_file", "Toi da " + _settings.SoNguyen(SettingKeys.UploadMaxFiles) + " file moi lan xin link.");
            }

            LabelingProject duAn = await _access.LayDeQuanLyAsync(projectId, caller, ct);
            duAn.KiemTraCoTheNapDuLieu();

            DateTimeOffset hetHan = _clock.GetUtcNow().Add(_settings.ThoiGian(SettingKeys.UploadLinkTtl));
            List<UploadSlotResponse> ketQua = new List<UploadSlotResponse>();

            foreach (UploadFileInput f in body.Files)
            {
                string ten = f.Name == null ? string.Empty : Path.GetFileName(f.Name.Trim());
                string duoi = Path.GetExtension(ten).ToLowerInvariant();

                if (ten.Length == 0)
                {
                    throw new InvalidValueException("thieu_ten_file", "Moi file can \"name\".");
                }

                if (!DuocPhep(duAn.Modality, duoi))
                {
                    throw new InvalidValueException(
                        "duoi_file_khong_hop_le",
                        "File '" + ten + "' khong hop voi du lieu " + duAn.Modality + ". Cho phep: "
                        + string.Join(", ", DuoiChoPhep[duAn.Modality]) + (DuoiChoPhep[duAn.Modality].Length > 0 ? ", " : string.Empty)
                        + string.Join(", ", DuoiManifest) + ".");
                }

                if (f.SizeBytes <= 0 || f.SizeBytes > _settings.SoLon(SettingKeys.UploadMaxFileBytes))
                {
                    throw new InvalidValueException("dung_luong_khong_hop_le", "File '" + ten + "': dung luong phai tu 1 byte den 5 GB.");
                }

                string khoa = projectId.ToString() + "/raw/" + Guid.CreateVersion7().ToString() + duoi;

                ketQua.Add(new UploadSlotResponse
                {
                    Name = ten,
                    Key = khoa,
                    UploadUrl = await _storage.TaoLinkTaiLenAsync(khoa),
                    ExpiresAt = hetHan,
                });
            }

            return ketQua;
        }

        private static bool DuocPhep(string modality, string duoi)
        {
            if (Array.IndexOf(DuoiManifest, duoi) >= 0)
            {
                return true;
            }

            string[]? ds;
            return DuoiChoPhep.TryGetValue(modality, out ds) && Array.IndexOf(ds, duoi) >= 0;
        }
    }
}
