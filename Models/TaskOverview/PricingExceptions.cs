using KpiMgmtApi.Models.Interfaces;

namespace KpiMgmtApi.Models.TaskOverview
{
    public class PricingExceptionsResponse : IMetricResponse
    {
        public List<PricingExceptions> Result { get; set; }
    }
    public class PricingExceptions
    {
        public string Record_Hour { get; set; }
        public int Record_Count { get; set; }
        public string description { get; set; }

    }
}
