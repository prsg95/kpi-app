using KpiMgmtApi.Models.Interfaces;

namespace KpiMgmtApi.Models.BusinessProcess
{
    public class TopBusinessProcessResponse : IMetricResponse
    {
        public List<TopBusinessProcess> Result { get; set; }
    }
    public class TopBusinessProcess
    {
        public string Record_Hour { get; set; }
        public int Record_Count { get; set; }
        public string Description { get; set; }
    }
}
