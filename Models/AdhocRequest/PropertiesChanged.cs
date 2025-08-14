using KpiMgmtApi.Models.Interfaces;

namespace KpiMgmtApi.Models.AdhocRequest
{
    public class PropertiesChangedResponse : IMetricResponse
    {
        public List<PropertiesChanged> Result { get; set; }
    }
    public class PropertiesChanged
    {
        public string Record_Hour { get; set; }
        public int Record_Count { get; set; }
        public string Property { get; set; }
    }
}
