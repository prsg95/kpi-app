using KpiMgmtApi.Models.Interfaces;

namespace KpiMgmtApi.Models.TaskOverview
{
    public class PricingPlausibilityResponse : IMetricResponse
    {
        public List<PricingPlausibility> Result { get; set; }
    }
    public class PricingPlausibility
    {
        public string Record_Hour { get; set; }
        public int Record_Count { get; set; }


    }
}
