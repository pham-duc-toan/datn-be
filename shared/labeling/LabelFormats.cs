using System.Collections.Generic;
using Crowd.Labeling.Formats;

namespace Crowd.Labeling
{
    /// <summary>
    /// Bang dang ky moi dinh dang nhan he thong hieu. Them loai nhan moi: viet
    /// lop cai ILabelFormat roi them MOT dong vao danh sach duoi.
    /// </summary>
    public static class LabelFormats
    {
        private static readonly IReadOnlyList<ILabelFormat> TatCa = new ILabelFormat[]
        {
            new ImageClassificationV1(),
        };

        public static ILabelFormat Lay(string taskType, int schemaVersion)
        {
            foreach (ILabelFormat f in TatCa)
            {
                if (f.TaskType == taskType && f.SchemaVersion == schemaVersion)
                {
                    return f;
                }
            }

            throw new LabelFormatException(
                "loai_nhan_chua_ho_tro",
                "Chua ho tro loai nhan '" + taskType + "' phien ban " + schemaVersion + ".");
        }

        /// <summary>Phien ban moi nhat cua mot loai nhan — dung khi client khong noi ro phien ban.</summary>
        public static int PhienBanMoiNhat(string taskType)
        {
            int moiNhat = 0;
            foreach (ILabelFormat f in TatCa)
            {
                if (f.TaskType == taskType && f.SchemaVersion > moiNhat)
                {
                    moiNhat = f.SchemaVersion;
                }
            }

            if (moiNhat == 0)
            {
                throw new LabelFormatException("loai_nhan_chua_ho_tro", "Chua ho tro loai nhan '" + taskType + "'.");
            }

            return moiNhat;
        }
    }
}
