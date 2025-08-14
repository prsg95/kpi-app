using KpiMgmtApi.Models.Interfaces;

namespace KpiMgmtApi.Models.DatabaseStats
{
    public class DataFilesResponse : IMetricResponse
    {
        public List<DataFiles> Result { get; set; }
    }
    public class DataFiles
    {
        public int File_Id { get; set; }
        public string File_Name { get; set; }
        public string Tablespace_Name { get; set; }
        public double Size_Gb { get; set; }
        public string Status { get; set; }
    }
}
