namespace KpiMgmtApi.Models
{
    public class Oem
    {
        public int EID { get; set; }
        public string PDBName { get; set; }
        public string NamedCred { get; set; }
        public string TargetId { get; set; }
        public Env Env { get; set; } // One-to-one relationship with Env
    }
}
