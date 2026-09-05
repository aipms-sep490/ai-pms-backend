using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Identity;

internal sealed class ProjectExecutionGuard(AipmsDbContext context) : IProjectExecutionGuard
{
    public async Task MustBeActiveAsync(long projectId, CancellationToken cancellationToken)
    {
        var project = await context.Projects
            .AsNoTracking()
            .Select(p => new { p.Id, p.Status })
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
            ?? throw new NotFoundException("Project", projectId);

        if (project.Status != "ACTIVE")
        {
            throw new ConflictException($"Project is not in ACTIVE state. Current state: {project.Status}. Mutations are restricted to ACTIVE projects.");
        }
    }

    public async Task MustBeActiveForMilestoneAsync(long milestoneId, CancellationToken cancellationToken)
    {
        var milestone = await context.Milestones
            .AsNoTracking()
            .Select(m => new { m.Id, m.ProjectId, ProjectStatus = m.Project.Status })
            .FirstOrDefaultAsync(m => m.Id == milestoneId, cancellationToken)
            ?? throw new NotFoundException("Milestone", milestoneId);

        if (milestone.ProjectStatus != "ACTIVE")
        {
            throw new ConflictException($"Project is not in ACTIVE state. Current state: {milestone.ProjectStatus}. Mutations are restricted to ACTIVE projects.");
        }
    }

    public async Task MustBeActiveForTaskAsync(long taskId, CancellationToken cancellationToken)
    {
        var task = await context.Tasks
            .AsNoTracking()
            .Select(t => new { t.Id, ProjectStatus = t.Milestone.Project.Status })
            .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken)
            ?? throw new NotFoundException("Task", taskId);

        if (task.ProjectStatus != "ACTIVE")
        {
            throw new ConflictException($"Project is not in ACTIVE state. Current state: {task.ProjectStatus}. Mutations are restricted to ACTIVE projects.");
        }
    }
}
