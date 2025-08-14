using KpiMgmtApi.Models.Interfaces;

namespace KpiMgmtApi.Models.AdhocRequest
{
    public class TotalPropertiesCountResponse : IMetricResponse
    {
        public List<TotalPropertiesCount> Result { get; set; }
    }
    public class TotalPropertiesCount
    {
        public string Record_Hour { get; set; }
        public int Record_Count { get; set; }

    }
}
