using System.Collections.Generic;
using Crowd.Admin.Api.Workers;
using Xunit;

namespace Crowd.Admin.Tests
{
    public sealed class DlqMonitorTests
    {
        [Fact]
        public void Doc_dlq_tu_management_api()
        {
            string json = "[{\"name\":\"ledger-svc.annotation-approved\",\"messages\":0},"
                + "{\"name\":\"ledger-svc.annotation-approved.dlq\",\"messages\":3},"
                + "{\"name\":\"admin-svc.audit.dlq\",\"messages\":0},"
                + "{\"name\":\"quality-svc.x.dlq\"}]";
            List<KeyValuePair<string, long>> ds = DlqMonitorWorker.DocDlq(json);
            Assert.Equal(3, ds.Count);
            Assert.Equal("ledger-svc.annotation-approved.dlq", ds[0].Key);
            Assert.Equal(3L, ds[0].Value);
            Assert.Equal(0L, ds[2].Value);
        }
    }
}
