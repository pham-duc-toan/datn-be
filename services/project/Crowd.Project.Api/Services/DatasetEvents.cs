using System;
using System.Collections.Generic;
using System.Linq;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.Contracts.Project;
using Crowd.Project.Domain.Datasets;

namespace Crowd.Project.Api.Services
{
    /// <summary>
    /// Phat dataset.ingested cho cac mau vua tao — dung chung cho duong ZIP
    /// (DatasetService) va duong manifest (DatasetIngestor).
    ///
    /// Chia LO de message nho (vai chuc KB); consumer biet du khi dem du BatchCount.
    /// </summary>
    public static class DatasetEvents
    {
        /// <summary>So mau moi lo dataset.ingested.</summary>
        public const int CoLo = 500;

        public static void PhatCacLo(ProjectEventPublisher events, Guid projectId, Guid datasetId, IReadOnlyList<Sample> mau, Caller caller)
        {
            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            if (mau == null)
            {
                throw new ArgumentNullException(nameof(mau));
            }

            int soLo = (mau.Count + CoLo - 1) / CoLo;

            for (int lo = 0; lo < soLo; lo++)
            {
                List<IngestedSample> trongLo = new List<IngestedSample>();
                foreach (Sample s in mau.Skip(lo * CoLo).Take(CoLo))
                {
                    trongLo.Add(new IngestedSample
                    {
                        SampleId = s.Id,
                        Modality = s.Modality,
                        StorageKey = s.StorageKey,
                        Content = s.Content,
                        Metadata = s.Metadata,
                    });
                }

                events.Phat(caller, new DatasetIngested
                {
                    ProjectId = projectId,
                    DatasetId = datasetId,
                    BatchIndex = lo,
                    BatchCount = soLo,
                    Samples = trongLo,
                });
            }
        }
    }
}
