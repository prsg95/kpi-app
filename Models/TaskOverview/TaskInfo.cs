using KpiMgmtApi.Models.Interfaces;

namespace KpiMgmtApi.Models.TaskOverview
{
    public class TaskInfoResponse : IMetricResponse
    {
        public List<TaskInfo> Result { get; set; }
    }
    public class TaskInfo
    {
        public string Task_Type { get; set; }
        public int Task_Count { get; set; }
        public string Record_Hour { get; set; }
    }
}
