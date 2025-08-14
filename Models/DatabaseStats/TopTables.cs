using KpiMgmtApi.Models.Interfaces;

namespace KpiMgmtApi.Models.DatabaseStats
{
    public class TopTablesReponse : IMetricResponse
    {
        public List<TopTables> Result { get; set; }
    }
    public class TopTables
    {
        public string owner { get; set; }
        public string table_name { get; set; }
        public string segment_type { get; set; }
        public double GB { get; set; }
        public string tablespace_name { get; set; }

    }
}
