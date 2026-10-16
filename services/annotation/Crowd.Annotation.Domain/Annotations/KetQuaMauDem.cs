using System;

namespace Crowd.Annotation.Domain.Annotations
{
    /// <summary>
    /// BO NHO DEM ket qua gop cua MOT mau (bang sample_results). Trang ket qua / file xuat truoc day gop lai
    /// MOI mau o moi lan goi (du an lon, doi tuong day thi vai giay). Nay moi mau giu ket qua lan tinh truoc
    /// kem DAU VAN TAY dau vao:
    ///     SHA-256(tap nhan + nguong ghep + F1 toi thieu + danh sach id nhan DA DUYET cua mau, da sap xep)
    /// Dau vao khong doi → dung lai; doi bat cu gi (duyet / tu choi / khieu nai / tranh chap lam thay doi
    /// tap nhan da duyet, admin doi nguong) → dau van tay lech → tinh lai mau do. Khong can ai "xoa cache"
    /// dung luc: cache SAI khong bao gio duoc dung, chi co cache CU bi bo qua.
    /// </summary>
    public sealed class KetQuaMauDem
    {
        private KetQuaMauDem()
        {
            Fingerprint = string.Empty;
            ToolsJson = string.Empty;
        }

        public Guid ProjectId { get; private set; }

        public Guid SampleId { get; private set; }

        public string Fingerprint { get; private set; }

        /// <summary>Ket qua gop tung cong cu (JSON) — nhu SampleResult.Tools.</summary>
        public string ToolsJson { get; private set; }

        public DateTimeOffset ComputedAt { get; private set; }

        public static KetQuaMauDem Tao(Guid projectId, Guid sampleId, string dauVanTay, string toolsJson, DateTimeOffset luc)
        {
            KetQuaMauDem k = new KetQuaMauDem();
            k.ProjectId = projectId;
            k.SampleId = sampleId;
            k.Fingerprint = dauVanTay;
            k.ToolsJson = toolsJson;
            k.ComputedAt = luc;
            return k;
        }
    }
}
