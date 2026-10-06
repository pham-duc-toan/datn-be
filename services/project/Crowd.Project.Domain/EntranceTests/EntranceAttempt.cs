using System;
using System.Collections.Generic;
using Crowd.Labeling;
using Crowd.Project.Domain.Common;

namespace Crowd.Project.Domain.EntranceTests
{
    /// <summary>
    /// Mot lan lam bai test dau vao cua mot labeler (FL-03).
    ///
    /// Luat:
    ///   - Toi da SoLanToiDa lan moi du an — khong cho thu mai de do dap an.
    ///   - Moi lan co han ThoiGianLamBai; nop tre tinh la truot.
    ///   - Chi cham cac cau DA GIAO trong lan nay; tra loi cau khac bi bo qua.
    ///   - Mot cau dung khi nhan tra loi KHOP dap an theo dinh dang nhan
    ///     (LabelPayload.KhopDapAn). Phan loai: trung tap lop, khong tinh thu tu;
    ///     multi-label thieu hay thua mot nhan deu la sai.
    /// </summary>
    public sealed class EntranceAttempt
    {
        public const int SoLanToiDa = 3;
        public static readonly TimeSpan ThoiGianLamBai = TimeSpan.FromMinutes(30);

        private List<Guid> _questionSampleIds;

        private EntranceAttempt()
        {
            _questionSampleIds = new List<Guid>();
        }

        public Guid Id { get; private set; }

        public Guid ProjectId { get; private set; }

        public Guid UserId { get; private set; }

        /// <summary>Cac mau duoc giao trong lan nay, theo dung thu tu hien cho labeler.</summary>
        public IReadOnlyList<Guid> QuestionSampleIds
        {
            get { return _questionSampleIds; }
        }

        public DateTimeOffset StartedAt { get; private set; }

        public DateTimeOffset ExpiresAt { get; private set; }

        /// <summary>null = chua nop.</summary>
        public DateTimeOffset? SubmittedAt { get; private set; }

        public int? ScorePercent { get; private set; }

        public bool? Passed { get; private set; }

        public static EntranceAttempt BatDau(
            Guid projectId,
            Guid userId,
            IReadOnlyList<Guid> cauHoi,
            int soLanDaLam,
            bool dangCoLanChuaNop,
            DateTimeOffset luc)
        {
            if (cauHoi == null || cauHoi.Count == 0)
            {
                throw new RuleViolationException("khong_co_cau_hoi", "Du an chua co cau hoi cho bai test.");
            }

            if (dangCoLanChuaNop)
            {
                throw new RuleViolationException(
                    "dang_lam_bai",
                    "Ban dang co mot bai test chua nop. Hay nop hoac doi het gio.");
            }

            if (soLanDaLam >= SoLanToiDa)
            {
                throw new RuleViolationException(
                    "het_luot_test",
                    "Da het " + SoLanToiDa + " luot lam bai test cua du an nay.");
            }

            EntranceAttempt a = new EntranceAttempt();
            a.Id = Guid.CreateVersion7();
            a.ProjectId = projectId;
            a.UserId = userId;
            a._questionSampleIds = new List<Guid>(cauHoi);
            a.StartedAt = luc;
            a.ExpiresAt = luc + ThoiGianLamBai;
            return a;
        }

        /// <summary>Con dang lam (chua nop, chua het gio) khong.</summary>
        public bool DangMo(DateTimeOffset luc)
        {
            return SubmittedAt == null && luc < ExpiresAt;
        }

        /// <summary>
        /// Nop bai va cham. dapAn chua dap an cua CAC CAU DUOC GIAO (service lay
        /// tu cau hoi vang). Tra ve true neu dau.
        /// </summary>
        public bool Nop(
            IReadOnlyDictionary<Guid, LabelPayload> traLoi,
            IReadOnlyDictionary<Guid, LabelPayload> dapAn,
            LabelSchema tapNhan,
            int nguongPhanTram,
            DateTimeOffset luc)
        {
            if (traLoi == null)
            {
                throw new ArgumentNullException(nameof(traLoi));
            }

            if (dapAn == null)
            {
                throw new ArgumentNullException(nameof(dapAn));
            }

            if (tapNhan == null)
            {
                throw new ArgumentNullException(nameof(tapNhan));
            }

            if (SubmittedAt != null)
            {
                throw new RuleViolationException("da_nop", "Bai test nay da nop roi.");
            }

            SubmittedAt = luc;

            if (luc >= ExpiresAt)
            {
                ScorePercent = 0;
                Passed = false;
                return false;
            }

            int soCauDung = 0;

            foreach (Guid cau in _questionSampleIds)
            {
                LabelPayload? cuaLabeler;
                LabelPayload? dung;

                if (!traLoi.TryGetValue(cau, out cuaLabeler) || !dapAn.TryGetValue(cau, out dung))
                {
                    continue;
                }

                // "Khop" do tung CONG CU quyet dinh: phan loai so tap lop, bbox so
                // IoU, transcription so ti le loi ky tu... (Crowd.Labeling).
                if (cuaLabeler.KhopDapAn(tapNhan, dung))
                {
                    soCauDung = soCauDung + 1;
                }
            }

            ScorePercent = soCauDung * 100 / _questionSampleIds.Count;
            Passed = ScorePercent.Value >= nguongPhanTram;
            return Passed.Value;
        }
    }
}
