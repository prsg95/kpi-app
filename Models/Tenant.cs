using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KpiMgmtApi.Models
{
    [Table("Tenant")] // Explicitly map the class to the Tenant table
    public class Tenant
    {
        [Key] // Specify that TID is the primary key
        [Column("TID")] // Explicit column mapping
        public string TID { get; set; }

        [Column("TName")] // Explicit column mapping
        public string? TName { get; set; }

        [Column("TShortName")] // Explicit column mapping
        public string TShortName { get; set; }

        [Column("TAccountName")]  // New column for account name
        public string TAccountName { get; set; }

        [Column("TAccountKey")]   // New column for account key
        public string TAccountKey { get; set; }

        public ICollection<Env> Envs { get; set; } // One-to-many relationship with Env
        public ICollection<TenantProduct> TenantProducts { get; set; }  // One-to-many relationship with TenantProduct

    }
}
