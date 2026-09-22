using System.Threading.Tasks;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal static class MilestoneTemplateLock
{
    // Catalog mutations and period pinning must serialize before reading mutable versions.
    internal static Task AcquireAsync(AipmsDbContext db, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync("""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource = N'milestone_template_catalog',
                @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
            IF @result < 0 THROW 51000, 'Cannot acquire milestone template catalog lock.', 1;
            """, ct);
}
