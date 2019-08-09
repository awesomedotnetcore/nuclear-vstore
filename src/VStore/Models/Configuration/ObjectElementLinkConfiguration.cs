using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NuClear.VStore.Models.Configuration
{
    public class ObjectElementLinkConfiguration : IEntityTypeConfiguration<ObjectElementLink>
    {
        public void Configure(EntityTypeBuilder<ObjectElementLink> builder)
        {
            builder.HasKey(e => new { e.ObjectId, e.ObjectVersionId, e.ElementId, e.ElementVersionId })
                   .HasName("am_elem_link_pkey");

            builder.ToTable("am_elem_link");

            builder.Property(e => e.ObjectId).HasColumnName("am_id");

            builder.Property(e => e.ObjectVersionId)
                   .HasColumnName("am_version")
                   .HasMaxLength(36);

            builder.Property(e => e.ElementId).HasColumnName("elem_id");

            builder.Property(e => e.ElementVersionId)
                   .HasColumnName("elem_version")
                   .HasMaxLength(36);

            builder.HasOne(d => d.Object)
                   .WithMany(p => p.ElementLinks)
                   .HasForeignKey(d => new { d.ObjectId, d.ObjectVersionId })
                   .HasConstraintName("am_elem_link_am_id_fkey");

            builder.HasOne(d => d.Element)
                   .WithMany(p => p.ObjectLinks)
                   .HasForeignKey(d => new { d.ElementId, d.ElementVersionId })
                   .HasConstraintName("am_elem_link_elem_id_fkey");
        }
    }
}