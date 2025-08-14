using KpiMgmtApi.Models.Interfaces;

namespace KpiMgmtApi.Models.AdhocRequest
{
    public class ManualhandledCaxResponse : IMetricResponse
    {
        public List<ManualhandledCax> Result { get; set; }
    }
    public class ManualhandledCax
    {
        public string Record_Hour { get; set; }
        public int Record_Count { get; set; }

    }
}
