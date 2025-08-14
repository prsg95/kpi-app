namespace KpiMgmtApi.Models
{
    public class Product
    {
        public int PID { get; set; }
        public string PName { get; set; }
        public ICollection<Env> Envs { get; set; } // One-to-many relationship with Env
        public ICollection<TenantProduct> TenantProducts { get; set; } // One-to-many relationship with TenantProduct

    }
}
