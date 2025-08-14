using KpiMgmtApi.Models.Interfaces;

namespace KpiMgmtApi.Models.DatabaseStats
{
    public class DatabaseStatusResponse : IMetricResponse
    {
        public List<DatabaseStatus> Result { get; set; }
    }
    public class DatabaseStatus
    {
        public string Name { get; set; }
        public string Open_Mode { get; set; }
        public string Restricted {  get; set; }
        public string Creation_Time { get; set; }
        public string Database_Uptime { get; set; }
        public double Total_Size_GB { get; set; }

    }
}
