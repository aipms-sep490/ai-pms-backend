using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    public DbSet<MeetingDecision> MeetingDecisions => Set<MeetingDecision>();
    public DbSet<MeetingActionItem> MeetingActionItems => Set<MeetingActionItem>();

    private static void ConfigureGovernanceBaseline(ModelBuilder b)
    {
        b.Entity<MeetingDecision>(e =>
        {
            e.ToTable("meeting_decisions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.MeetingId).HasColumnName("meeting_id");
            e.Property(x => x.Content).HasColumnName("content").HasMaxLength(4000);
            e.Property(x => x.DecidedBy).HasColumnName("decided_by");
            e.Property(x => x.DecidedAt).HasColumnName("decided_at").HasPrecision(0);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasPrecision(0);
            e.HasOne<M.Meeting>().WithMany().HasForeignKey(x => x.MeetingId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.DecidedBy).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<MeetingActionItem>(e =>
        {
            e.ToTable("meeting_action_items");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.MeetingId).HasColumnName("meeting_id");
            e.Property(x => x.Title).HasColumnName("title").HasMaxLength(500);
            e.Property(x => x.Description).HasColumnName("description").HasMaxLength(4000);
            e.Property(x => x.AssigneeUserId).HasColumnName("assignee_user_id");
            e.Property(x => x.DueAt).HasColumnName("due_at").HasPrecision(0);
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
            e.Property(x => x.CreatedBy).HasColumnName("created_by");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasPrecision(0);
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasPrecision(0);
            e.HasIndex(x => new { x.MeetingId, x.Status, x.DueAt }).HasDatabaseName("ix_meeting_action_items_meeting");
            e.HasIndex(x => new { x.AssigneeUserId, x.Status }).HasDatabaseName("ix_meeting_action_items_assignee");
            e.HasOne<M.Meeting>().WithMany().HasForeignKey(x => x.MeetingId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.AssigneeUserId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
