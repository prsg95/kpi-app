using KpiMgmtApi.Models.Interfaces;

namespace KpiMgmtApi.Models.BusinessProcess
{
    public class InstrumentScrubbedResponse : IMetricResponse
    {
        public List<InstrumentScrubbed> Result { get; set; }
    }
    public class InstrumentScrubbed
    {
        public string Record_Hour { get; set; }
        public int Record_Count { get; set; }
    }
}
