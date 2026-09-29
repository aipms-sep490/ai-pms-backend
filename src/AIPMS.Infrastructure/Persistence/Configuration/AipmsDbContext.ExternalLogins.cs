using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    public virtual DbSet<UserExternalLogin> UserExternalLogins => Set<UserExternalLogin>();
    public virtual DbSet<ExternalLoginChallenge> ExternalLoginChallenges => Set<ExternalLoginChallenge>();

    private static void ConfigureExternalLogins(ModelBuilder builder)
    {
        builder.Entity<UserExternalLogin>(e =>
        {
            e.ToTable("user_external_logins");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(255).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.Email).HasColumnName("email").HasMaxLength(255);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.Provider, x.Subject }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.Provider }).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        });
        builder.Entity<ExternalLoginChallenge>(e =>
        {
            e.ToTable("external_login_challenges");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.Purpose).HasColumnName("purpose").HasMaxLength(10).IsUnicode(false);
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.NonceHash).HasColumnName("nonce_hash").HasColumnType("binary(64)");
            e.Property(x => x.BrowserHash).HasColumnName("browser_hash").HasColumnType("binary(64)");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            e.Property(x => x.ConsumedAt).HasColumnName("consumed_at");
            e.HasIndex(x => x.ExpiresAt);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
