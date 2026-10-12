using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigureWorkingAgreements(ModelBuilder b)
    {
        b.Entity<WorkingAgreement>(e => {
            e.ToTable("project_working_agreements"); e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            e.HasIndex(x => new { x.ProjectId, x.Version }).IsUnique();
            e.HasMany(x => x.Members).WithOne().HasForeignKey(x => x.AgreementId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Generated.Models.Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Generated.Models.User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<AgreementMember>(e => {
            e.ToTable("agreement_members"); e.HasKey(x => new { x.AgreementId, x.UserId });
            e.HasOne<Generated.Models.TeamMember>().WithMany().HasForeignKey(x => x.TeamMemberId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Generated.Models.User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        });
        foreach (var type in new[] { typeof(WorkingAgreement), typeof(AgreementMember) })
            foreach (var p in b.Entity(type).Metadata.GetProperties())
            {
                p.SetColumnName(System.Text.RegularExpressions.Regex.Replace(p.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
                if (p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?)) p.SetPrecision(0);
            }
    }
}
