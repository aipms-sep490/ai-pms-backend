using System.Threading.Tasks;
using AIPMS.Application.Features.Tasks.Abstractions;
using AIPMS.Application.Features.Tasks.Queries;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class TaskBoardSummaryReader(AipmsDbContext db) : ITaskBoardSummaryReader
{
    public async Task<bool> ScopeBelongsToProject(long projectId, long? milestoneId, long? sprintId, CancellationToken ct)
    {
        if (milestoneId.HasValue && !await db.Milestones.AnyAsync(m => m.Id == milestoneId && m.ProjectId == projectId, ct)) return false;
        if (sprintId.HasValue && !await db.Set<Sprint>().AnyAsync(s => s.Id == sprintId && s.ProjectId == projectId, ct)) return false;
        return true;
    }

    public async Task<IReadOnlyList<TaskBoardStatusCount>> CountByStatus(long projectId, long? milestoneId, long? sprintId, CancellationToken ct)
    {
        var query = db.Tasks.AsNoTracking().Where(t => t.Milestone.ProjectId == projectId);
        if (milestoneId.HasValue) query = query.Where(t => t.MilestoneId == milestoneId);
        if (sprintId.HasValue) query = query.Where(t => t.SprintId == sprintId);
        var counts = await query.GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.LongCount() }).ToArrayAsync(ct);
        var byStatus = counts.ToDictionary(x => x.Status, x => x.Count);
        return [.. new[] { "TODO", "IN_PROGRESS", "BLOCKED", "IN_REVIEW", "DONE", "CANCELLED" }
            .Select(status => new TaskBoardStatusCount(status, byStatus.GetValueOrDefault(status)))];
    }
}
