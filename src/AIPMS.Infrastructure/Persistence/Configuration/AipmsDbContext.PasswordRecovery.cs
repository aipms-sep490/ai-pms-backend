using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    public DbSet<PasswordRecoveryRequest> PasswordRecoveryRequests => Set<PasswordRecoveryRequest>();

    private static void ConfigurePasswordRecovery(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().Property(x => x.PasswordRecoveryInvalidBefore)
            .HasColumnName("password_recovery_invalid_before").HasPrecision(7);
        modelBuilder.Entity<PasswordRecoveryRequest>(e =>
        {
            e.ToTable("password_recovery_requests");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.EmailHash).HasColumnName("email_hash").HasMaxLength(64).IsUnicode(false);
            e.Property(x => x.ProtectedPayload).HasColumnName("protected_payload");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasPrecision(7);
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at").HasPrecision(7);
            e.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at").HasPrecision(7);
            e.Property(x => x.AttemptCount).HasColumnName("attempt_count");
            e.Property(x => x.LeaseToken).HasColumnName("lease_token");
            e.Property(x => x.LeaseUntil).HasColumnName("lease_until").HasPrecision(7);
            e.Property(x => x.ResetTokenId).HasColumnName("reset_token_id");
            e.Property(x => x.CompletedAt).HasColumnName("completed_at").HasPrecision(7);
            e.Property(x => x.ErrorCode).HasColumnName("error_code").HasMaxLength(40).IsUnicode(false);
            e.HasIndex(x => new { x.Status, x.NextAttemptAt, x.LeaseUntil }).HasDatabaseName("ix_password_recovery_claim");
            e.HasIndex(x => new { x.EmailHash, x.Id }).IsDescending(false, true).HasDatabaseName("ix_password_recovery_email");
            e.HasOne<PasswordResetToken>().WithMany().HasForeignKey(x => x.ResetTokenId).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
