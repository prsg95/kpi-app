namespace KpiMgmtApi.Models
{
    public class Metric
    {
        public int ID { get; set; } // Primary key
        public string MetricName { get; set; } // Name of the metric
        public string MetricType { get; set; } // Type of the metric (e.g., Application, Infrastructure)
        public DateTime CreatedAt { get; set; } // Timestamp when the metric was created

        // Navigation property for related SubMetrics
        public ICollection<SubMetric> SubMetrics { get; set; }

    }
}
