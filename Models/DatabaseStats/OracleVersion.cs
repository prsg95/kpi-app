using KpiMgmtApi.Models.Interfaces;

namespace KpiMgmtApi.Models.DatabaseStats
{
    public class OracleVersionResponse : IMetricResponse
    {
        public List<OracleVersion> Result { get; set; }
    }
    public class OracleVersion
    {
        public string Banner {  get; set; }
    }
}
