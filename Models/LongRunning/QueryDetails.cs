using KpiMgmtApi.Models.Interfaces;

namespace KpiMgmtApi.Models.LongRunning
{
    public class QueryDetailsResponse : IMetricResponse
    {
        public List<QueryDetails> Result { get; set; }
    }
    public class QueryDetails
    {
        public int Sid { get; set; }
        public int SerialNumber { get; set; }
        public string? Username { get; set; }
        public string? Status { get; set; }
        public string? Logon_Time { get; set; }
        public string? Sql_Text { get; set; }

    }
}
