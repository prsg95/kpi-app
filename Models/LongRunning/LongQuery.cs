using KpiMgmtApi.Models.Interfaces;

namespace KpiMgmtApi.Models.LongRunning
{
    public class LongQueryResponse : IMetricResponse
    {
        public List<LongQuery> Result { get; set; }
    }
    public class LongQuery
    {
        public string Record_Hour { get; set; }
        public int Record_Count { get; set; }

    }
}
