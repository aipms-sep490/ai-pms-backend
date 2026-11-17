using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigureDepartmentDocumentTemplates(ModelBuilder model)
    {
        model.Entity<DepartmentDocumentTemplate>(e =>
        {
            e.ToTable("department_document_templates");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(60).IsUnicode(false);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.FileName).HasMaxLength(255);
            e.Property(x => x.ContentType).HasMaxLength(150).IsUnicode(false);
            e.Property(x => x.Sha256).HasMaxLength(64).IsUnicode(false).IsFixedLength();
            e.Property(x => x.StorageKey).HasMaxLength(80).IsUnicode(false);
            e.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            e.Property(x => x.UploadedAt).HasPrecision(0);
            e.Property(x => x.PublishedAt).HasPrecision(0);
            e.HasIndex(x => new { x.DepartmentId, x.MajorId, x.ProjectPeriodId, x.Code, x.Version }, "ux_department_document_templates_version").IsUnique();
            e.HasIndex(x => new { x.MajorId, x.ProjectPeriodId, x.Status, x.Code, x.Version }, "ix_department_document_templates_scope");
            foreach (var p in e.Metadata.GetProperties())
                p.SetColumnName(System.Text.RegularExpressions.Regex.Replace(p.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
        });
    }
}
