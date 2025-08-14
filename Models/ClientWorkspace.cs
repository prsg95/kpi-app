namespace KpiMgmtApi.Models
{
    public class ClientWorkspace
    {
        public int ID { get; set; }
        public string ClientName { get; set; }
        public string Environment { get; set; } // 'PROD' or 'NON-PROD'
        public string WorkspaceID { get; set; }
    }
}
