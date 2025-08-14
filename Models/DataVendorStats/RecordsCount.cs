using KpiMgmtApi.Models.Interfaces;
namespace KpiMgmtApi.Models.DataVendorStats
{
    public class RecordsCountResponse : IMetricResponse
    {
        public List<RecordsCount> Result { get; set; }
    }
    public class RecordsCount
    {
        public string Record_Hour { get; set; }
        public int Record_Count { get; set; }

    }
}
