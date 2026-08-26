using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Repositories;

public sealed class ProjectProgressDataReader(AipmsDbContext context) : IProjectProgressDataReader
{
    public async Task<ProjectProgressFacts?> GetProjectProgressFactsAsync(
        long projectId,
        CancellationToken cancellationToken)
    {
        var project = await context.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(static p => new
            {
                p.Id,
                p.Status,
                p.TeamId,
                TeamMemberCount = p.Team.TeamMembers.Count(static tm => tm.LeftAt == null)
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (project is null)
        {
            return null;
        }

        var milestones = await context.Milestones
            .AsNoTracking()
            .Where(m => m.ProjectId == projectId)
            .OrderBy(static m => m.SortOrder)
            .ThenBy(static m => m.Id)
            .Select(static m => new MilestoneFact(
                m.Id,
                m.Title,
                m.Status,
                m.StartDate,
                m.DueDate,
                m.SortOrder))
            .ToListAsync(cancellationToken);

        var tasks = await context.Tasks
            .AsNoTracking()
            .Where(t => t.Milestone.ProjectId == projectId)
            .Select(static t => new TaskFact(
                t.Id,
                t.MilestoneId,
                t.Title,
                t.Status,
                t.Priority,
                t.StartAt,
                t.DueAt,
                t.CompletedAt,
                t.TaskAssignees.Count))
            .ToListAsync(cancellationToken);

        var progressReports = await context.ProgressReports
            .AsNoTracking()
            .Where(pr => pr.ProjectId == projectId)
            .OrderByDescending(static pr => pr.PeriodEnd)
            .Select(static pr => new ProgressReportFact(
                pr.Id,
                pr.ReportType,
                pr.PeriodStart,
                pr.PeriodEnd,
                pr.Status,
                pr.SubmittedAt))
            .ToListAsync(cancellationToken);

        var meetings = await context.Meetings
            .AsNoTracking()
            .Where(m => m.ProjectId == projectId)
            .Select(static m => new MeetingFact(
                m.Id,
                m.Title,
                m.Status,
                m.StartAt,
                m.EndAt))
            .ToListAsync(cancellationToken);

        return new ProjectProgressFacts(
            project.Id,
            project.Status,
            project.TeamId,
            project.TeamMemberCount,
            milestones,
            tasks,
            progressReports,
            meetings);
    }
}
