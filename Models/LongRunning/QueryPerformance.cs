using KpiMgmtApi.Models.Interfaces;

namespace KpiMgmtApi.Models.LongRunning
{
    public class QueryPerformanceResponse : IMetricResponse
    {
        public List<QueryPerformance> Result { get; set; }
    }
    public class QueryPerformance
    {
        public string? SqlId { get; set; }
        public string? SqlText { get; set; }
        public double ElapsedSeconds { get; set; }
        public double CpuSeconds { get; set; }
        public long BufferGets { get; set; }
        public long DiskReads { get; set; }

    }
}
