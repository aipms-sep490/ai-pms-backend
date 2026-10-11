using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigureCold(ModelBuilder b)
    {
        b.Entity<ColdReportTemplate>(e => {
            e.ToTable("cold_report_templates"); e.HasKey(x => x.Id); e.Property(x => x.Name).HasMaxLength(200);
            e.HasIndex(x => new { x.MajorId, x.ProjectPeriodId, x.Version }).IsUnique();
            e.HasOne<Generated.Models.Major>().WithMany().HasForeignKey(x => x.MajorId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Generated.Models.ProjectPeriod>().WithMany().HasForeignKey(x => x.ProjectPeriodId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<ColdPackage>(e => {
            e.ToTable("cold_packages"); e.HasKey(x => x.Id); e.Property(x => x.Status).HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.ConcurrencyToken).IsConcurrencyToken(); e.HasIndex(x => x.ProjectId).IsUnique();
            e.HasOne<Generated.Models.Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Generated.Models.ProjectPeriod>().WithMany().HasForeignKey(x => x.ProjectPeriodId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<ColdChecklistItem>(e => {
            e.ToTable("cold_checklist_items"); e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.ReportType).HasMaxLength(80).IsUnicode(false);
            e.Property(x => x.Scope).HasMaxLength(20).IsUnicode(false);
            e.HasOne<ColdPackage>().WithMany().HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<ColdReportTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.PackageId, x.ReportType, x.MajorId, x.StudentId }).IsUnique();
        });
        b.Entity<ColdSubmission>(e => {
            e.ToTable("cold_submissions"); e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasMaxLength(20).IsUnicode(false); e.Property(x => x.Note).HasMaxLength(2000);
            e.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            e.HasIndex(x => new { x.ChecklistItemId, x.Version }).IsUnique();
            e.HasOne<ColdChecklistItem>().WithMany().HasForeignKey(x => x.ChecklistItemId).OnDelete(DeleteBehavior.NoAction);
            e.HasMany(x => x.Files).WithOne().HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<ColdSubmissionFile>(e => {
            e.ToTable("cold_submission_files"); e.HasKey(x => new { x.SubmissionId, x.FileId });
            e.HasIndex(x => x.FileId).IsUnique();
            e.HasOne<Generated.Models.File>().WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.NoAction);
            e.Property(x => x.FileName).HasMaxLength(255); e.Property(x => x.StorageKey).HasMaxLength(500);
            e.Property(x => x.ContentType).HasMaxLength(150).IsUnicode(false); e.Property(x => x.Sha256).HasMaxLength(64).IsUnicode(false);
        });
        foreach (var type in new[] { typeof(ColdReportTemplate), typeof(ColdPackage), typeof(ColdChecklistItem), typeof(ColdSubmission), typeof(ColdSubmissionFile) })
            foreach (var p in b.Entity(type).Metadata.GetProperties())
            {
                p.SetColumnName(System.Text.RegularExpressions.Regex.Replace(p.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
                if (p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?)) p.SetPrecision(0);
            }
    }
}
