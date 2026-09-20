using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    public virtual DbSet<Models.TeamLeaderChangeRequest> TeamLeaderChangeRequests { get; set; } = null!;

    private static void ConfigureTeamLeaderChangeRequests(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Models.TeamLeaderChangeRequest>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_team_leader_change_requests");
            entity.ToTable("team_leader_change_requests");
            entity.HasIndex(e => new { e.TeamId, e.Status }, "ix_team_leader_change_requests_team_status");
            entity.HasIndex(e => new { e.MentorProfileId, e.Status }, "ix_team_leader_change_requests_mentor_status");
            entity.HasIndex(e => e.TeamId, "ux_team_leader_change_requests_pending")
                .IsUnique().HasFilter("([status]=N'PENDING')");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.TeamId).HasColumnName("team_id");
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.RequestedBy).HasColumnName("requested_by");
            entity.Property(e => e.CurrentLeaderUserId).HasColumnName("current_leader_user_id");
            entity.Property(e => e.NewLeaderUserId).HasColumnName("new_leader_user_id");
            entity.Property(e => e.MentorProfileId).HasColumnName("mentor_profile_id");
            entity.Property(e => e.Status).HasMaxLength(20).HasColumnName("status");
            entity.Property(e => e.RequestMessage).HasMaxLength(2000).HasColumnName("request_message");
            entity.Property(e => e.ResponseMessage).HasMaxLength(2000).HasColumnName("response_message");
            entity.Property(e => e.RequestedAt).HasPrecision(0).HasColumnName("requested_at");
            entity.Property(e => e.RespondedAt).HasPrecision(0).HasColumnName("responded_at");
            entity.Property(e => e.CreatedAt).HasPrecision(0).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasPrecision(0).HasColumnName("updated_at");
            entity.HasOne(e => e.Project).WithMany().HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull).HasConstraintName("fk_team_leader_change_requests_project");
            entity.HasOne(e => e.Team).WithMany().HasForeignKey(e => e.TeamId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_team_leader_change_requests_team");
            entity.HasOne(e => e.RequestedByNavigation).WithMany().HasForeignKey(e => e.RequestedBy)
                .OnDelete(DeleteBehavior.ClientSetNull).HasConstraintName("fk_team_leader_change_requests_requested_by");
            entity.HasOne(e => e.CurrentLeaderUser).WithMany().HasForeignKey(e => e.CurrentLeaderUserId)
                .OnDelete(DeleteBehavior.ClientSetNull).HasConstraintName("fk_team_leader_change_requests_current_leader");
            entity.HasOne(e => e.NewLeaderUser).WithMany().HasForeignKey(e => e.NewLeaderUserId)
                .OnDelete(DeleteBehavior.ClientSetNull).HasConstraintName("fk_team_leader_change_requests_new_leader");
            entity.HasOne(e => e.MentorProfile).WithMany().HasForeignKey(e => e.MentorProfileId)
                .OnDelete(DeleteBehavior.ClientSetNull).HasConstraintName("fk_team_leader_change_requests_mentor_profile");
        });
    }
}
