using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NuClear.VStore.Models.Configuration
{
    public class TemplateConfiguration : IEntityTypeConfiguration<Template>
    {
        public void Configure(EntityTypeBuilder<Template> builder)
        {
            builder.HasKey(e => new { e.Id, e.VersionId })
                   .HasName("template_pkey");

            builder.ToTable("template");

            builder.HasIndex(e => new { e.Id, e.VersionIndex })
                   .HasName("template_id_version_index_uindex")
                   .IsUnique();

            builder.Property(e => e.Id).HasColumnName("id");

            builder.Property(e => e.VersionId)
                   .HasColumnName("version")
                   .HasMaxLength(36)
                   .HasDefaultValueSql("gen_random_uuid()");

            builder.Property(e => e.VersionIndex)
                   .HasColumnName("version_index")
                   .ValueGeneratedNever();

            builder.Property(e => e.Data)
                   .HasColumnName("data")
                   .HasColumnType("jsonb");

            builder.Property(e => e.Author)
                   .HasColumnName("author")
                   .IsRequired();

            builder.Property(e => e.AuthorLogin)
                   .HasColumnName("author_login")
                   .IsRequired();

            builder.Property(e => e.AuthorName)
                   .HasColumnName("author_name")
                   .IsRequired();

            builder.Property(e => e.LastModified)
                   .HasColumnName("last_modified")
                   .IsRequired();
        }
    }
}