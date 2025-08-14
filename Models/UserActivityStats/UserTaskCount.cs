using KpiMgmtApi.Models.Interfaces;

namespace KpiMgmtApi.Models.UserActivityStats
{
    public class UserTaskCountResponse : IMetricResponse
    {
        public List<UserTaskCount> Result { get; set; }
    }
    public class UserTaskCount
    {
        public string UserName { get; set; }
        public int Record_Count { get; set; }
        public string Record_Hour { get; set; }
    }
}
