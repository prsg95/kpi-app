using KpiMgmtApi.Models.Interfaces;

namespace KpiMgmtApi.Models.DataVendorStats
{

    public class SwiftMessagesResponse : IMetricResponse
    {
        public List<SwiftMessages> Result { get; set; }
    }
    public class SwiftMessages
    {
        public string Record_Hour { get; set; }
        public int Record_Count { get; set; }
    }
}
