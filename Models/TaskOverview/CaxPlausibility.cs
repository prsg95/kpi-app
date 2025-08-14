using KpiMgmtApi.Models.Interfaces;

namespace KpiMgmtApi.Models.TaskOverview
{
    public class CaxPlausibilityResponse : IMetricResponse
    {
        public List<CaxPlausibility> Result { get; set; }
    }
    public class CaxPlausibility
    {
        public string Record_Hour { get; set; }
        public int Record_Count { get; set; }

    }
}
