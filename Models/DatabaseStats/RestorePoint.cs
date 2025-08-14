namespace KpiMgmtApi.Models.DatabaseStats
{
    public class RestorePointResponse
    {
        public List<RestorePoint> Result { get; set; }
    }

    public class RestorePoint
    {
        public string Name { get; set; }
        public long Scn { get; set; }
        public long Time { get; set; }
        public int Database_Incarnation { get; set; }
        public string Guarantee_Flashback_Database { get; set; }
        public long Storage_Size { get; set; }

        public DateTime ConvertedTime => ConvertUnixTimestampToDateTime(Time);
        public double StorageSizeGB => Math.Round(Storage_Size / (1024.0 * 1024 * 1024), 2);

        // Helper method to convert Unix timestamp to DateTime
        private static DateTime ConvertUnixTimestampToDateTime(long unixTimestampMilliseconds)
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return epoch.AddMilliseconds(unixTimestampMilliseconds).ToLocalTime(); // Convert to local time if needed
        }
    }
}
