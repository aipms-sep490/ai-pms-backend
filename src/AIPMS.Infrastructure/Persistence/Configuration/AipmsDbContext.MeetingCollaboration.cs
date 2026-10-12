using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigureMeetingCollaboration(ModelBuilder b)
    {
        b.Entity<M.Meeting>(e => {
            e.Property(x => x.SeriesId).HasColumnName("series_id");
            e.Property(x => x.OccurrenceNumber).HasColumnName("occurrence_number");
            e.Property(x => x.Recurrence).HasColumnName("recurrence").HasMaxLength(12).IsUnicode(false);
            e.Property(x => x.NotesVersion).HasColumnName("notes_version");
            e.Property(x => x.NotesLockedAt).HasColumnName("notes_locked_at").HasPrecision(0);
            e.Property(x => x.NotesLockedBy).HasColumnName("notes_locked_by");
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.NotesLockedBy).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.SeriesId, x.OccurrenceNumber }).IsUnique().HasFilter("[series_id] IS NOT NULL");
        });
        b.Entity<M.MeetingParticipant>(e => {
            e.Property(x => x.RsvpStatus).HasColumnName("rsvp_status").HasMaxLength(12).IsUnicode(false);
            e.Property(x => x.RsvpToken).HasColumnName("rsvp_token").IsConcurrencyToken();
        });
        b.Entity<MeetingNotesVersion>(e => {
            e.ToTable("meeting_notes_versions"); e.HasKey(x => x.Id);
            e.HasOne<M.Meeting>().WithMany().HasForeignKey(x => x.MeetingId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.ChangedBy).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.MeetingId, x.Version }).IsUnique();
            foreach (var p in e.Metadata.GetProperties())
            {
                p.SetColumnName(System.Text.RegularExpressions.Regex.Replace(p.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
                if (p.ClrType == typeof(DateTime)) p.SetPrecision(0);
            }
        });
    }
}
