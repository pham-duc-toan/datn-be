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
        /// <param name="coLo">So mau moi event — setting dataset.event_batch_size.</param>
        public static void PhatCacLo(ProjectEventPublisher events, Guid projectId, Guid datasetId, IReadOnlyList<Sample> mau, Caller caller, int coLo)
        {
            if (coLo < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(coLo));
            }

            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            if (mau == null)
            {
                throw new ArgumentNullException(nameof(mau));
            }

            int soLo = (mau.Count + coLo - 1) / coLo;

            for (int lo = 0; lo < soLo; lo++)
            {
                List<IngestedSample> trongLo = new List<IngestedSample>();
                foreach (Sample s in mau.Skip(lo * coLo).Take(coLo))
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
