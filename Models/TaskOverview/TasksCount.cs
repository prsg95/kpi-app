namespace KpiMgmtApi.Models.TaskOverview
{
    public class TasksCount
    {
        public int Total_Count { get; set; }
        public int? Created { get; set; }
        public int? Completed { get; set; }
        public int? Pending { get; set; }

    }
}
