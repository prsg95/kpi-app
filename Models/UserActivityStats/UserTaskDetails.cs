using KpiMgmtApi.Models.Interfaces;

namespace KpiMgmtApi.Models.UserActivityStats
{
    public class UserTaskDetailsResponse : IMetricResponse
    {
        public List<UserTaskDetails> Result { get; set; }
    }
    public class UserTaskDetails
    {
        public string UserName { get; set; }
        public string Task_Details { get; set; }
        public string Task_Created { get; set; }
        public string Record_Hour { get; set; }
        public string Task_Comments { get; set; }
        public string Type_Of_TaskAgent { get; set; }


    }
}
