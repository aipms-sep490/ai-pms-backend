using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigureExecutionConcurrency(ModelBuilder b)
    {
        b.Entity<M.Task>().Property(x => x.ConcurrencyToken).HasColumnName("concurrency_token").HasDefaultValueSql("NEWSEQUENTIALID()").IsConcurrencyToken();
        b.Entity<M.Milestone>().Property(x => x.ConcurrencyToken).HasColumnName("concurrency_token").HasDefaultValueSql("NEWSEQUENTIALID()").IsConcurrencyToken();
        b.Entity<M.ProgressReport>().Property(x => x.ConcurrencyToken).HasColumnName("concurrency_token").HasDefaultValueSql("NEWSEQUENTIALID()").IsConcurrencyToken();
        b.Entity<M.Meeting>().Property(x => x.ConcurrencyToken).HasColumnName("concurrency_token").HasDefaultValueSql("NEWSEQUENTIALID()").IsConcurrencyToken();
    }
}
