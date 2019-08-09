using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NuClear.VStore.Models.Configuration
{
    public class ObjectElementConfiguration : IEntityTypeConfiguration<ObjectElement>
    {
        public void Configure(EntityTypeBuilder<ObjectElement> builder)
        {
            builder.HasKey(e => new { e.Id, e.VersionId })
                   .HasName("am_elem_pkey");

            builder.ToTable("am_elem");

            builder.Property(e => e.Id).HasColumnName("id");

            builder.Property(e => e.VersionId)
                   .HasColumnName("version")
                   .HasMaxLength(36)
                   .HasDefaultValueSql("gen_random_uuid()");

            builder.Property(e => e.Data)
                   .HasColumnName("data")
                   .HasColumnType("jsonb");

            builder.Property(e => e.LastModified)
                   .HasColumnName("last_modified")
                   .IsRequired();
        }
    }
}