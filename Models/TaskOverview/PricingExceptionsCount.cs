using KpiMgmtApi.Models.Interfaces;

namespace KpiMgmtApi.Models.TaskOverview
{
    public class PricingExceptionsCountResponse : IMetricResponse
    {
        public List<PricingExceptionsCount> Result { get; set; }
    }
    public class PricingExceptionsCount
    {
        public string Record_Hour { get; set; }
        public int Record_Count { get; set; }

    }
}
