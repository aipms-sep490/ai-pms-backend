using AIPMS.Application.Abstractions.Projects;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class ProjectActivationService(AipmsDbContext db) : IProjectActivationService
{
    public async Task ApplyMilestoneTemplateAsync(long projectId, long actorUserId, DateTime activatedAt, CancellationToken cancellationToken = default)
    {
        if (await db.ProjectMilestoneTemplateApplications.AnyAsync(a => a.ProjectId == projectId, cancellationToken)) return;
        var project = await db.Projects.Include(p => p.Team).SingleAsync(p => p.Id == projectId, cancellationToken);
        var period = await db.ProjectPeriods.AsNoTracking()
            .Where(p => p.AcademicSemesterId == project.Team.AcademicSemesterId && p.PeriodType == "EXECUTION" && p.Status == "ACTIVE")
            .OrderByDescending(p => p.Id).FirstOrDefaultAsync(cancellationToken);
        if (period?.MilestoneTemplateVersionId is not long versionId) return;
        var version = await db.MilestoneTemplateVersions.Include(v => v.Items)
            .SingleOrDefaultAsync(v => v.Id == versionId && v.Status == "PUBLISHED", cancellationToken);
        if (version is null) return;
        db.ProjectMilestoneTemplateApplications.Add(new ProjectMilestoneTemplateApplication { ProjectId = projectId, MilestoneTemplateVersionId = version.Id, AppliedBy = actorUserId, AppliedAt = activatedAt });
        foreach (var item in version.Items.OrderBy(i => i.SortOrder))
        {
            db.Milestones.Add(new Milestone
            {
                ProjectId = projectId, Title = item.Title, Description = item.Description,
                StartDate = item.StartOffsetDays.HasValue ? DateOnly.FromDateTime(activatedAt.AddDays(item.StartOffsetDays.Value)) : null,
                DueDate = item.DueOffsetDays.HasValue ? DateOnly.FromDateTime(activatedAt.AddDays(item.DueOffsetDays.Value)) : null,
                Status = "PLANNED", SortOrder = item.SortOrder, CreatedBy = actorUserId, CreatedAt = activatedAt, UpdatedAt = activatedAt
            });
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
