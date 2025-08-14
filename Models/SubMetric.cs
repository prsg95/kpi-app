namespace KpiMgmtApi.Models
{
    public class SubMetric
    {
        public int ID { get; set; } // Primary key
        public string Sub_MetricName { get; set; } // Sub_Metric Namne
        public string Query { get; set; } // SQL query for the sub-metric

        // Foreign key linking to the Metric
        public int MetricID { get; set; } // Foreign key to the Metric table
        public Metric Metric { get; set; } // Navigation property for the related Metric

    }
}
