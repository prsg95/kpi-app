using System.Text.Json.Serialization;

namespace KpiMgmtApi.Models.VmStats
{
    public class VmMetricsResponse
    {
        [JsonPropertyName("cost")]
        public int Cost { get; set; }

        [JsonPropertyName("timespan")]
        public string Timespan { get; set; }

        [JsonPropertyName("interval")]
        public string Interval { get; set; }

        [JsonPropertyName("value")]
        public List<MetricValue> Value { get; set; }

        [JsonPropertyName("namespace")]
        public string Namespace { get; set; }

        [JsonPropertyName("resourceregion")]
        public string ResourceRegion { get; set; }

    }

    public class MetricValue
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("name")]
        public MetricName Name { get; set; }

        [JsonPropertyName("displayDescription")]
        public string DisplayDescription { get; set; }

        [JsonPropertyName("unit")]
        public string Unit { get; set; }

        [JsonPropertyName("timeseries")]
        public List<TimeSeries> Timeseries { get; set; }
    }

    public class MetricName
    {
        [JsonPropertyName("value")]
        public string Value { get; set; }

        [JsonPropertyName("localizedValue")]
        public string LocalizedValue { get; set; }
    }

    public class TimeSeries
    {
        [JsonPropertyName("metadatavalues")]
        public List<object> MetadataValues { get; set; } // Can be expanded if needed

        [JsonPropertyName("data")]
        public List<MetricData> Data { get; set; }
    }

    public class MetricData
    {
        [JsonPropertyName("timeStamp")]
        public DateTime TimeStamp { get; set; }

        [JsonPropertyName("average")]
        public double Average { get; set; }

        [JsonPropertyName("maximum")]
        public double? Maximum { get; set; }
        
        [JsonPropertyName("minimum")]
        public double? Minimum { get; set; }


    }


}
