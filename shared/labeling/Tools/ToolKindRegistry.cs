using System;
using System.Collections.Generic;

namespace Crowd.Labeling.Tools
{
    /// <summary>Bang tra loai cong cu → hanh vi. Them cong cu moi: them mot dong.</summary>
    internal static class ToolKindRegistry
    {
        private static readonly Dictionary<string, IToolKind> TatCa = TaoBang();

        public static IToolKind Lay(string kind)
        {
            IToolKind? k;
            if (!TatCa.TryGetValue(kind, out k))
            {
                throw new LabelFormatException("cong_cu_chua_ho_tro", "Chua ho tro cong cu '" + kind + "'.");
            }

            return k;
        }

        private static Dictionary<string, IToolKind> TaoBang()
        {
            Dictionary<string, IToolKind> d = new Dictionary<string, IToolKind>(StringComparer.Ordinal);
            foreach (IToolKind k in new IToolKind[]
            {
                new ClassificationTool(),
                new BboxTool(),
                new PolygonTool(),
                new SpanTool(),
                new TranscriptionTool(),
                new TemporalSegmentTool(),
                new PairwiseTool(),
            })
            {
                d[k.Kind] = k;
            }

            return d;
        }
    }
}
