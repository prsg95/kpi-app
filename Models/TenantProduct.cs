namespace KpiMgmtApi.Models
{
    public class TenantProduct
    {
        public string TID { get; set; }
        public int PID { get; set; }

        public Tenant Tenant { get; set; }
        public Product Product { get; set; }
    }
}
