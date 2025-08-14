using KpiMgmtApi.Models.Interfaces;

namespace KpiMgmtApi.Models.DatabaseStats
{
    public class DbIndexResponse : IMetricResponse
    {
        public List<DbIndex> Result { get; set; }
    }
    public class DbIndex
    {
        public string OBJECT_NAME { get; set; }
        public string OBJECT_TYPE { get; set; }
        public string Table_Name { get; set; }
        public decimal SizeGB { get; set; }

    }
}
