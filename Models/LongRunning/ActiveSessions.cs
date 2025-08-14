using KpiMgmtApi.Models.Interfaces;

namespace KpiMgmtApi.Models.LongRunning
{
    public class ActiveSessionsResponse : IMetricResponse
    {
        public List<ActiveSessions> Result { get; set; }
    }
    public class ActiveSessions
    {
        public int Sid { get; set; }
        public int SerialNumber { get; set; }
        public string? Username { get; set; }
        public string? Status { get; set; }
        public string? SchemaName { get; set; }
        public string? Logon_Time { get; set; }

    }
}
