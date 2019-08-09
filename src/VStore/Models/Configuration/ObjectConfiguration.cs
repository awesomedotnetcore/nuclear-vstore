using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NuClear.VStore.Models.Configuration
{
    public class ObjectConfiguration : IEntityTypeConfiguration<Object>
    {
        public void Configure(EntityTypeBuilder<Object> builder)
        {
            builder.HasKey(e => new { e.Id, e.VersionId })
                   .HasName("am_pkey");

            builder.ToTable("am");

            builder.HasIndex(e => new { e.Id, e.VersionIndex })
                   .HasName("am_id_version_index_uindex")
                   .IsUnique();

            builder.Property(e => e.Id).HasColumnName("id");

            builder.Property(e => e.VersionId)
                   .HasColumnName("version")
                   .HasMaxLength(36)
                   .HasDefaultValueSql("gen_random_uuid()");

            builder.Property(e => e.Data)
                   .HasColumnName("data")
                   .HasColumnType("jsonb");

            builder.Property(e => e.TemplateId).HasColumnName("template_id");

            builder.Property(e => e.TemplateVersionId)
                   .IsRequired()
                   .HasColumnName("template_version_id")
                   .HasMaxLength(36);

            builder.Property(e => e.VersionIndex)
                   .HasColumnName("version_index")
                   .ValueGeneratedNever();

            builder.Property(e => e.Author)
                   .HasColumnName("author")
                   .IsRequired();

            builder.Property(e => e.AuthorLogin)
                   .HasColumnName("author_login")
                   .IsRequired();

            builder.Property(e => e.AuthorName)
                   .HasColumnName("author_name")
                   .IsRequired();

            builder.Property(e => e.ModifiedElements)
                   .HasColumnName("modified_elements")
                   .IsRequired();

            builder.Property(e => e.LastModified)
                   .HasColumnName("last_modified")
                   .IsRequired();

            builder.HasOne(d => d.Template)
                   .WithMany(p => p.Objects)
                   .HasForeignKey(d => new { d.TemplateId, d.TemplateVersionId })
                   .HasConstraintName("am_template_id_fkey");
        }
    }
}