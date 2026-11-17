using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigureUserAvatars(ModelBuilder model)
    {
        model.Entity<UserAvatar>(e =>
        {
            e.ToTable("user_avatars");
            e.HasKey(x => x.UserId);
            e.Property(x => x.UserId).HasColumnName("user_id").ValueGeneratedNever();
            e.Property(x => x.StorageKey).HasColumnName("storage_key").HasMaxLength(80).IsUnicode(false);
            e.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.SizeBytes).HasColumnName("size_bytes");
            e.Property(x => x.Sha256).HasColumnName("sha256").HasMaxLength(64).IsUnicode(false).IsFixedLength();
            e.Property(x => x.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasPrecision(0);
            e.HasOne<AIPMS.Infrastructure.Persistence.Generated.Models.User>().WithOne()
                .HasForeignKey<UserAvatar>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
