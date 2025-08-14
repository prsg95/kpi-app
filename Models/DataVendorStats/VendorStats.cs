using KpiMgmtApi.Models.Interfaces;

namespace KpiMgmtApi.Models.DataVendorStats
{
    public class VendorStatResponse : IMetricResponse
    {
        public List<VendorStats> Result { get; set; }
    }
    public class VendorStats
    {
        public string Record_Hour { get; set; }
        public string Incoming_Source_Name { get; set; }
        public int Record_Count { get; set; }
    }
}
