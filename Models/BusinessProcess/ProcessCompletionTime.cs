using KpiMgmtApi.Models.Interfaces;

namespace KpiMgmtApi.Models.BusinessProcess
{
    public class ProcessCompletionTimeResponse : IMetricResponse
    {
        public List<ProcessCompletionTime> Result { get; set; }
    }
    public class ProcessCompletionTime
    {
        public string Record_Hour { get; set; }
        public int Record_Count { get; set; }
        public double Avg_Time_Taken_By_Process { get; set; }
    }
}
