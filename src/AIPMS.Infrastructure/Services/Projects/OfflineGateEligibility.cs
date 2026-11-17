using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal static class OfflineGateEligibility
{
    public static async Task RequirePreviousGateAndWindow(AipmsDbContext db, long projectId,
        int sequence, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var gates = await db.ProjectGateInstances.AsNoTracking()
            .Where(g => g.ProjectId == projectId && (g.Sequence == sequence - 1 || g.Sequence == sequence))
            .Select(g => new { g.Sequence, g.Status, g.WindowStart, g.WindowEnd }).ToArrayAsync(ct);
        if (!gates.Any(g => g.Sequence == sequence - 1 && g.Status == "PASSED"))
            throw new ConflictException($"Review Gate {sequence - 1} must be passed before scheduling.",
                $"REVIEW_GATE_{sequence - 1}_NOT_PASSED");
        var target = gates.SingleOrDefault(g => g.Sequence == sequence);
        if (target?.WindowStart is null || target.WindowEnd is null || target.WindowEnd <= target.WindowStart)
            throw new ConflictException($"Review Gate {sequence} needs a valid schedule window.",
                sequence == 4 ? "DEFENSE_GATE_WINDOW_REQUIRED" : "REVIEW_GATE_3_WINDOW_REQUIRED");
        if (start.UtcDateTime < target.WindowStart || end.UtcDateTime > target.WindowEnd)
            throw new ConflictException("The session is outside the configured Gate window.",
                sequence == 4 ? "DEFENSE_OUTSIDE_GATE_WINDOW" : "REVIEW_GATE_3_OUTSIDE_WINDOW");
    }
}
