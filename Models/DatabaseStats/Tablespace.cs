using KpiMgmtApi.Models.Interfaces;

namespace KpiMgmtApi.Models.DatabaseStats
{

    public class TablespaceResponse : IMetricResponse
    {
        public List<Tablespace> Result { get; set; }
        public double Total_Size { get; set; }
        public double Total_Used_Size { get; set; }
    }
    public class Tablespace
    {
        public string Tablespace_Name { get; set; }
        public string Ts_Type { get; set; }
        public double Ts_Size { get; set; }
        public double Us_Size { get; set; }
        public double Fr_Size { get; set; }
        public double PercentUsed { get; set; }
        public double PercentFree { get; set; }
    }
}
