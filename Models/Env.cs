namespace KpiMgmtApi.Models
{
    public class Env
    {
        public int EID { get; set; }
        public string EName { get; set; }
        public string TID { get; set; }
        public string SchemaName { get; set; }
        public int Pid { get; set; }
        public string RgName { get; set; }
        public Tenant Tenant { get; set; }
        public Oem Oem { get; set; } // One-to-one relationship with Oem
        public Product Product { get; set; }
    }
}
