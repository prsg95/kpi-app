namespace KpiMgmtApi.Models.DataVendorStats
{
    public class CountResponse
    {
        public List<CountResult> Result { get; set; }

    }
    public class CountResult
    {
        public int COUNT { get; set; }  // Adjusted to match the expected JSON response
    }

}
