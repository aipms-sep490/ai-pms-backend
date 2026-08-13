using AIPMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AIPMS.Infrastructure.Persistence.Configurations;

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("Projects");
        builder.HasKey(project => project.Id);

        builder.Property(project => project.Name)
            .HasMaxLength(250)
            .IsRequired();

        builder.Property(project => project.Status)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();
    }
}
