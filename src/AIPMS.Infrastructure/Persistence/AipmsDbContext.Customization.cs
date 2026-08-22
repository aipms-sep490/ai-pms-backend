using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Models.SupervisorAssignment>()
            .ToTable(table => table.HasTrigger("tr_supervisor_assignments_require_accepted_request"));
        modelBuilder.Entity<Models.SupervisorRequest>()
            .ToTable(table => table.HasTrigger("tr_supervisor_requests_protect_active_assignment"));
    }
}
