using KpiMgmtApi.Models.Interfaces;
using KpiMgmtApi.Models.Interfaces;
namespace KpiMgmtApi.Models.DataVendorStats
{
    public class VendorRequestCountResponse : IMetricResponse
    {
        public List<VendorRequestCount> Result { get; set; }
    }
    public class VendorRequestCount
    {
        public string Record_Hour { get; set; }
        public int Record_Count { get; set; }

    }
}
