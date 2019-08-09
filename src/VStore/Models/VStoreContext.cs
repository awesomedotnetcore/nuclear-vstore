using Microsoft.EntityFrameworkCore;

using NuClear.VStore.Models.Configuration;

namespace NuClear.VStore.Models
{
    public class VStoreContext : DbContext
    {
        public VStoreContext(DbContextOptions<VStoreContext> options)
            : base(options)
        {
        }

        public virtual DbSet<Object> Objects { get; set; }
        public virtual DbSet<ObjectElement> ObjectElements { get; set; }
        public virtual DbSet<ObjectElementLink> ObjectElementLinks { get; set; }
        public virtual DbSet<Template> Templates { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasPostgresExtension("pgcrypto");

            modelBuilder.ApplyConfiguration(new ObjectConfiguration());
            modelBuilder.ApplyConfiguration(new ObjectElementConfiguration());
            modelBuilder.ApplyConfiguration(new ObjectElementLinkConfiguration());
            modelBuilder.ApplyConfiguration(new TemplateConfiguration());
        }
    }
}