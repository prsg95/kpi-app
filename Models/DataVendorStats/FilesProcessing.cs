using KpiMgmtApi.Models.Interfaces;

namespace KpiMgmtApi.Models.DataVendorStats
{
    public class FilesProcessingResponse : IMetricResponse
    {
        public List<FilesProcessing> Result { get; set; }
    }
    public class FilesProcessing
    {
        public string Record_Hour { get; set; }
        public int Record_Count { get; set; }

    }
}
