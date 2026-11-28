using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigureTasksSoftDeleteFilter(ModelBuilder modelBuilder)
    {
        // TK-W2-02: Global query filter ensures soft-deleted tasks (deleted_at IS NOT NULL)
        // are automatically excluded from progress percentages, board summaries, milestones, sprints, and calendars.
        // RestoreAsync uses .IgnoreQueryFilters() explicitly.
        modelBuilder.Entity<AIPMS.Infrastructure.Persistence.Generated.Models.Task>()
            .HasQueryFilter(t => t.DeletedAt == null);
    }
}
