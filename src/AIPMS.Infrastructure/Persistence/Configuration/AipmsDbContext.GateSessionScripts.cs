using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigureGateSessionScripts(ModelBuilder b)
    {
        b.Entity<GateSessionScript>(e =>
        {
            e.ToTable("gate_session_scripts");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ProjectGateInstanceId).IsUnique();
            e.HasOne<ProjectGateInstance>().WithMany().HasForeignKey(x => x.ProjectGateInstanceId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.NoAction);
            e.Property(x => x.Mode).HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.LiveKitRoomName).HasMaxLength(100);
            e.Property(x => x.LiveKitSessionId).HasMaxLength(100);
            e.Property(x => x.Verdict).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.SignedBySecretary).HasMaxLength(100);
            e.Property(x => x.SignedByChair).HasMaxLength(100);
            e.Property(x => x.PublishedAt).HasPrecision(0);
            e.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
        });
        foreach (var p in b.Entity<GateSessionScript>().Metadata.GetProperties())
            p.SetColumnName(System.Text.RegularExpressions.Regex.Replace(p.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
    }
}
