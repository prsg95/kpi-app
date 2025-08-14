using Microsoft.EntityFrameworkCore;
using KpiMgmtApi.Models;
using Microsoft.Identity.Client;

namespace KpiMgmtApi.Data
{
    public class TenantInfoContext : DbContext
    {
        public TenantInfoContext(DbContextOptions<TenantInfoContext> options) : base(options) { }

        public DbSet<Tenant> Tenants { get; set; }
        public DbSet<Env> Envs { get; set; }
        public DbSet<Oem> Oems { get; set; }
        public DbSet<Query> Queries { get; set; }
        public DbSet<Metric> Metrics { get; set; }
        public DbSet<SubMetric> SubMetrics { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<TenantProduct> TenantProducts { get; set; }
        public DbSet<Pdbs> Pdbs { get; set; }
        public DbSet<ClientWorkspace> ClientWorkspaces { get; set; }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Tenant>()
                .ToTable("Tenant");

            modelBuilder.Entity<Tenant>()
                .Property(t => t.TID)
                .HasColumnName("TID"); // No change, keeping the same column name

            modelBuilder.Entity<Tenant>()
                .Property(t => t.TName)
                .HasColumnName("TName"); // No change, keeping the same column name

            modelBuilder.Entity<Tenant>()
                .Property(t => t.TShortName)
                .HasColumnName("TShortName"); // No change, keeping the same column name

            modelBuilder.Entity<Env>()
                .ToTable("Environment");

            modelBuilder.Entity<Oem>()
                .ToTable("OEM");
            
            modelBuilder.Entity<Pdbs>()
                .ToTable("Pdbs");

            modelBuilder.Entity<Metric>()
                .ToTable("Metrics")
                .HasKey(m => m.ID);

            modelBuilder.Entity<SubMetric>()
                .ToTable("Sub_Metrics")
                .HasKey(sm => sm.ID);
            
            modelBuilder.Entity<Product>()
                .ToTable("Products")
                .HasKey(p => p.PID);

            modelBuilder.Entity<ClientWorkspace>()
                .ToTable("ClientWorkspace")
                .HasKey(cw => cw.ID);

            modelBuilder.Entity<ClientWorkspace>()
                .Property(cw => cw.ClientName)
                .HasColumnName("ClientName");

            modelBuilder.Entity<ClientWorkspace>()
                .Property(cw => cw.WorkspaceID)
                .HasColumnName("WorkspaceID");

            modelBuilder.Entity<ClientWorkspace>()
                .Property(cw => cw.Environment)
                .HasColumnName("Environment");

            modelBuilder.Entity<Tenant>()
                .HasKey(t => t.TID);

            modelBuilder.Entity<Env>()
                .HasKey(e => e.EID);

            modelBuilder.Entity<Oem>()
                .HasKey(o => o.EID);
            
            modelBuilder.Entity<Pdbs>()
                .HasKey(pd => pd.PdbName);

            modelBuilder.Entity<Query>().HasNoKey();


            // Define the one-to-many relationship between Tenant and Env
            modelBuilder.Entity<Tenant>()
                .HasMany(t => t.Envs)
                .WithOne(e => e.Tenant)
                .HasForeignKey(e => e.TID);
            
            // Define the one-to-one relationship between Env and Oem
            modelBuilder.Entity<Env>()
                .HasOne(e => e.Oem)
                .WithOne(o => o.Env)
                .HasForeignKey<Oem>(o => o.EID);

            modelBuilder.Entity<SubMetric>()
                .HasOne(sm => sm.Metric)
                .WithMany(m => m.SubMetrics)
                .HasForeignKey(sm => sm.MetricID);

            // Define the one-to-many relationship between Product and Env
            modelBuilder.Entity<Product>()
                .HasMany(p => p.Envs)
                .WithOne(e => e.Product)
                .HasForeignKey(e => e.Pid);


            modelBuilder.Entity<TenantProduct>()
                .HasKey(tp => new { tp.TID, tp.PID });  // Composite primary key for TenantProduct

            modelBuilder.Entity<TenantProduct>()
                .HasOne(tp => tp.Tenant)
                .WithMany(t => t.TenantProducts) // Tenant can have many TenantProducts
                .HasForeignKey(tp => tp.TID); // Foreign key in TenantProduct pointing to Tenant

            modelBuilder.Entity<TenantProduct>()
                .HasOne(tp => tp.Product)
                .WithMany(p => p.TenantProducts) // Product can have many TenantProducts
                .HasForeignKey(tp => tp.PID); // Foreign key in TenantProduct pointing to Product
                

        }
    }
}
