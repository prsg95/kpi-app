using KpiMgmtApi.Models.Interfaces;

namespace KpiMgmtApi.Models.AdhocRequest
{
    public class PartiesChangedResponse : IMetricResponse
    {
        public List<PartiesChanged> Result { get; set; }
    }
    public class PartiesChanged
    {
        public string Record_Hour { get; set; }
        public int Record_Count { get; set; }

    }
}
