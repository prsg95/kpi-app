using KpiMgmtApi.Models.Interfaces;

namespace KpiMgmtApi.Models.LongRunning
{
    public class LongJobsResponse : IMetricResponse
    {
        public List<LongJobs> Result { get; set; }
    }
    public class LongJobs
    {
        public string Job_Name { get; set; }
        public string Record_Hour { get; set; }
        public int Record_Count { get; set; }

    }
}
