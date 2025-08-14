using KpiMgmtApi.Models.Interfaces;

namespace KpiMgmtApi.Models.AdhocRequest
{
    public class ManualRequestResponse : IMetricResponse
    {
        public List<ManualRequest> Result { get; set; }
    }
    public class ManualRequest
    {
        public string Record_Hour { get; set; }
        public int Record_Count { get; set; }

    }
}
