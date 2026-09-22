using System.Data;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Projects;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class ProjectActivationService(AipmsDbContext db) : IProjectActivationService
{
    public async Task ApplyMilestoneTemplateAsync(long projectId, long actorUserId, DateTime activatedAt, CancellationToken cancellationToken = default)
    {
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        try
        {
            var persisted = await db.Projects.FromSqlInterpolated($"SELECT * FROM dbo.projects WITH (UPDLOCK, HOLDLOCK) WHERE id = {projectId}")
                .AsNoTracking().SingleOrDefaultAsync(cancellationToken)
                ?? throw new NotFoundException("Project", projectId);
            var project = await db.Projects.Include(p => p.Team).SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken)
                ?? throw new NotFoundException("Project", projectId);
            if (project.Status != "ACTIVE") throw new ConflictException("Only ACTIVE projects can initialize milestones.");
            if (!persisted.MilestonesInitialized)
            {
                var period = await db.ProjectPeriods.AsNoTracking()
                    .Where(p => p.AcademicSemesterId == project.Team.AcademicSemesterId && p.PeriodType == "EXECUTION"
                        && (p.Status == "ACTIVE" || p.Status == "UPCOMING") && p.EndAt > activatedAt)
                    .OrderBy(p => p.StartAt).ThenBy(p => p.Id).FirstOrDefaultAsync(cancellationToken);
                if (period?.MilestoneTemplateId is not null)
                {
                    var version = await db.MilestoneTemplateVersions.Include(v => v.Items)
                        .SingleOrDefaultAsync(v => v.Id == period.MilestoneTemplateVersionId
                            && v.MilestoneTemplateId == period.MilestoneTemplateId && v.Status == "PUBLISHED", cancellationToken)
                        ?? throw new ConflictException("The execution period must have a pinned published template version.");
                    if (version.Items.Count == 0) throw new ConflictException("The assigned template version has no milestones.");
                    if (!await db.ProjectMilestoneTemplateApplications.AnyAsync(a => a.ProjectId == projectId, cancellationToken))
                    {
                        db.ProjectMilestoneTemplateApplications.Add(new M.ProjectMilestoneTemplateApplication
                        { ProjectId = projectId, MilestoneTemplateVersionId = version.Id, AppliedBy = actorUserId, AppliedAt = activatedAt });
                        foreach (var item in version.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.Id))
                            db.Milestones.Add(new M.Milestone
                            {
                                ProjectId = projectId, Title = item.Title, Description = item.Description,
                                StartDate = item.StartOffsetDays.HasValue ? DateOnly.FromDateTime(activatedAt.AddDays(item.StartOffsetDays.Value)) : null,
                                DueDate = item.DueOffsetDays.HasValue ? DateOnly.FromDateTime(activatedAt.AddDays(item.DueOffsetDays.Value)) : null,
                                Status = "PLANNED", SortOrder = item.SortOrder, CreatedBy = actorUserId, CreatedAt = activatedAt, UpdatedAt = activatedAt
                            });
                    }
                }
                // Remember activation even without a template, so retries cannot add a later plan.
                project.MilestonesInitialized = true;
                await db.SaveChangesAsync(cancellationToken);
            }
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
