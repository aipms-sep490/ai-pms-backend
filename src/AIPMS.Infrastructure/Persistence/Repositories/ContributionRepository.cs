using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Features.Contributions.Abstractions;
using AIPMS.Application.Features.Contributions.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Repositories;

public sealed class ContributionRepository(AipmsDbContext context) : IContributionRepository
{
    public async Task<IReadOnlyList<ContributionMemberDto>> GetProjectSummaryAsync(long projectId, CancellationToken ct)
    {
        var members = await context.Projects.Where(p => p.Id == projectId).SelectMany(p => p.Team.TeamMembers)
            .Where(m => m.LeftAt == null).Select(m => new { m.UserId, Name = m.User.FullName }).AsNoTracking().ToListAsync(ct);
        var taskRows = await context.Tasks.Where(t => t.Milestone.ProjectId == projectId).SelectMany(t => t.TaskAssignees)
            .Select(a => new { a.UserId, a.Task.Status }).ToListAsync(ct);
        var reports = await context.ProgressReports.Where(r => r.ProjectId == projectId && r.SubmittedAt != null)
            .GroupBy(r => r.SubmittedBy).Select(g => new { UserId = g.Key, Count = g.Count() }).ToListAsync(ct);
        var meetings = await context.MeetingParticipants.Where(p => p.Meeting.ProjectId == projectId && p.AttendanceStatus == "ATTENDED")
            .GroupBy(p => p.UserId).Select(g => new { UserId = g.Key, Count = g.Count() }).ToListAsync(ct);
        var versions = await context.DeliverableVersions.Where(v => v.Deliverable.ProjectId == projectId)
            .GroupBy(v => v.SubmittedBy).Select(g => new { UserId = g.Key, Count = g.Count() }).ToListAsync(ct);
        return members.Select(m =>
        {
            var tasks = taskRows.Where(t => t.UserId == m.UserId).ToList();
            var done = tasks.Count(t => t.Status == "DONE");
            var report = reports.SingleOrDefault(x => x.UserId == m.UserId)?.Count ?? 0;
            var meeting = meetings.SingleOrDefault(x => x.UserId == m.UserId)?.Count ?? 0;
            var version = versions.SingleOrDefault(x => x.UserId == m.UserId)?.Count ?? 0;
            var evidence = report + meeting + version;
            var score = tasks.Count + evidence;
            return new ContributionMemberDto(m.UserId, m.Name, tasks.Count, done, report, meeting, version, score, evidence);
        }).ToArray();
    }
}
