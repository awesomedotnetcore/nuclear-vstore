using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using NuClear.VStore.Models;

using Object = NuClear.VStore.Models.Object;

namespace VStore.UnitTests
{
    public class InMemoryContext : VStoreContext
    {
        public InMemoryContext(DbContextOptions<VStoreContext> options) : base(options)
        {
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);
            optionsBuilder.ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning));
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // https://github.com/aspnet/EntityFrameworkCore/issues/7960
            modelBuilder.Entity<Object>(b => b.Ignore(e => e.ModifiedElements));
            modelBuilder.Entity<ObjectElement>(entity => entity.Property(e => e.VersionId).ValueGeneratedOnAdd());
            modelBuilder.Entity<Object>(entity => entity.Property(e => e.VersionId).ValueGeneratedOnAdd());
        }
    }
}