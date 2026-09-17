using System.Data;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Contributions.Abstractions;
using AIPMS.Application.Features.Contributions.DTOs;
using AIPMS.Application.Features.Contributions.Services;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Repositories;

public sealed class ContributionRepository(AipmsDbContext context) : IContributionRepository
{
    public async Task<T> InProjectTransactionAsync<T>(long projectId, Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            // Serialize rebuilds with each other and with project state transitions. Source reads remain
            // protected until snapshot AND audit commit, so one snapshot describes one database state.
            var project = await context.Projects.FromSqlInterpolated(
                $"SELECT * FROM dbo.projects WITH (UPDLOCK, HOLDLOCK) WHERE id = {projectId}")
                .AsNoTracking().SingleOrDefaultAsync(ct);
            if (project is null) throw new NotFoundException("Project", projectId);
            var result = await action(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            throw;
        }
    }

    public Task<bool> IsActiveUserAsync(long actorId, CancellationToken ct) =>
        context.Users.AnyAsync(u => u.Id == actorId && u.Status == "ACTIVE", ct);

    public Task<bool> CanRebuildAsync(long projectId, long actorId, CancellationToken ct) =>
        context.Users.AnyAsync(u => u.Id == actorId && u.Status == "ACTIVE"
            && (u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.Admin)
                || (u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.DepartmentStaff)
                    && u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive
                    && context.ProjectMajors.Any(m => m.ProjectId == projectId && m.Major.DepartmentId == u.DepartmentId))), ct);

    private Task<string> Status(long projectId, CancellationToken ct) =>
        context.Projects.Where(p => p.Id == projectId).Select(p => p.Status).SingleAsync(ct);

    private Task<ContributionSnapshot?> Latest(long projectId, CancellationToken ct) =>
        context.ContributionSnapshots.AsNoTracking().Where(s => s.ProjectId == projectId)
            .OrderByDescending(s => s.Id).FirstOrDefaultAsync(ct);

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static ContributionCapture Read(ContributionSnapshot snapshot) => snapshot.SnapshotJson.StartsWith('[')
        ? new("activity-v1", JsonSerializer.Deserialize<ContributionMemberDto[]>(snapshot.SnapshotJson)!, [])
        : JsonSerializer.Deserialize<ContributionCapture>(snapshot.SnapshotJson)!;

    private static ContributionSummaryDto Stored(ContributionSnapshot snapshot)
    {
        var capture = Read(snapshot);
        return ContributionScoring.Summarize(capture.Members) with
        {
            RuleVersion = capture.RuleVersion, SnapshotHash = snapshot.SnapshotHash, SnapshotAt = Utc(snapshot.SnapshotAt)
        };
    }

    public async Task<ContributionSummaryDto> GetSummaryAsync(long projectId, bool storedOnly, CancellationToken ct)
    {
        if (storedOnly || await Status(projectId, ct) == "ARCHIVED")
            return Stored(await Latest(projectId, ct) ?? throw new NotFoundException("Contribution snapshot", projectId));
        return ContributionScoring.Summarize((await Capture(projectId, ct)).Members);
    }

    public async Task<IReadOnlyList<ContributionEvidenceDto>> GetEvidenceAsync(long projectId, long userId, CancellationToken ct)
    {
        ContributionCapture capture;
        if (await Status(projectId, ct) == "ARCHIVED")
        {
            capture = Read(await Latest(projectId, ct) ?? throw new NotFoundException("Contribution snapshot", projectId));
            if (capture.RuleVersion == "activity-v1")
                throw new ConflictException("This legacy snapshot has no frozen evidence. Live evidence cannot be rebuilt for an archived project.");
        }
        else capture = await Capture(projectId, ct);
        if (!capture.Members.Any(m => m.UserId == userId)) throw new NotFoundException("Project member", userId);
        return capture.Evidence.Single(e => e.UserId == userId).Items;
    }

    public async Task<ContributionRebuildResult> RebuildSnapshotAsync(long projectId, DateTime snapshotAt, CancellationToken ct)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Contribution rebuild requires the project transaction.");
        if (await Status(projectId, ct) == "ARCHIVED") throw new ConflictException("Archived contributions are read-only.");
        var capture = await Capture(projectId, ct);
        if (capture.Members.Count == 0) throw new ConflictException("A contribution snapshot requires team membership history.");
        var json = JsonSerializer.Serialize(capture);
        var latest = await Latest(projectId, ct);
        if (latest is not null && latest.SnapshotJson == json) return new(Stored(latest), false);
        // Include predecessor identity so A -> B -> A creates a new generation instead of reviving stale A.
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes((latest?.SnapshotHash ?? "") + "\n" + json)));
        snapshotAt = new DateTime(snapshotAt.Ticks - snapshotAt.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        foreach (var member in capture.Members)
            context.ContributionSnapshots.Add(new()
            {
                ProjectId = projectId, UserId = member.UserId, SnapshotAt = snapshotAt, SnapshotHash = hash,
                ActivityScore = member.ActivityScore, EvidenceCount = member.EvidenceCount, SnapshotJson = json
            });
        await context.SaveChangesAsync(ct);
        return new(ContributionScoring.Summarize(capture.Members) with { SnapshotHash = hash, SnapshotAt = snapshotAt }, true);
    }

    private sealed record Activity(long UserId, string SourceType, long SourceId, string Label, DateTime OccurredAt, double Credit);

    private async Task<ContributionCapture> Capture(long projectId, CancellationToken ct)
    {
        // Retain departed members and attribute events only to a recorded membership interval.
        var memberships = await context.Projects.Where(p => p.Id == projectId).SelectMany(p => p.Team.TeamMembers)
            .Select(m => new { m.UserId, m.User.FullName, m.JoinedAt, m.LeftAt }).AsNoTracking().ToListAsync(ct);
        bool MemberAt(long userId, DateTime at) => memberships.Any(m => m.UserId == userId
            && m.JoinedAt <= at && (!m.LeftAt.HasValue || at <= m.LeftAt.Value));

        var assignments = await context.TaskAssignees.Where(a => a.Task.Milestone.ProjectId == projectId)
            .Select(a => new { a.UserId, a.AssignedAt, a.TaskId, a.Task.Title, a.Task.Status, a.Task.CompletedAt })
            .AsNoTracking().ToListAsync(ct);
        var activities = new List<Activity>();
        foreach (var task in assignments.Where(a => a.Status == "DONE" && a.CompletedAt.HasValue
                     && a.AssignedAt <= a.CompletedAt && MemberAt(a.UserId, a.CompletedAt.Value)).GroupBy(a => a.TaskId))
        {
            var assignees = task.DistinctBy(a => a.UserId).ToArray();
            activities.AddRange(assignees.Select(a => new Activity(a.UserId, "TASK", a.TaskId, a.Title, a.CompletedAt!.Value, 1d / assignees.Length)));
        }
        activities.AddRange(await context.ProgressReports.Where(r => r.ProjectId == projectId
                && (r.Status == "SUBMITTED" || r.Status == "REVIEWED") && r.SubmittedAt != null)
            .Select(r => new Activity(r.SubmittedBy, "PROGRESS_REPORT", r.Id, r.ReportType, r.SubmittedAt!.Value, 1)).ToListAsync(ct));
        activities.AddRange(await context.MeetingParticipants.Where(p => p.Meeting.ProjectId == projectId
                && p.Meeting.Status == "COMPLETED" && p.AttendanceStatus == "ATTENDED")
            .Select(p => new Activity(p.UserId, "MEETING", p.MeetingId, p.Meeting.Title, p.Meeting.StartAt, 1)).ToListAsync(ct));
        activities.AddRange(await context.DeliverableVersions.Where(v => v.Deliverable.ProjectId == projectId)
            .Select(v => new Activity(v.SubmittedBy, "DELIVERABLE_VERSION", v.Id, v.Deliverable.Title, v.SubmittedAt, 1)).ToListAsync(ct));
        // File evidence explains existing work without increasing credit merely by uploading more files.
        activities.AddRange(await context.Files.Where(f =>
                (f.DeliverableVersion != null && f.DeliverableVersion.Deliverable.ProjectId == projectId)
                || (f.ProgressReport != null && f.ProgressReport.ProjectId == projectId && f.ProgressReport.SubmittedAt != null
                    && (f.ProgressReport.Status == "SUBMITTED" || f.ProgressReport.Status == "REVIEWED"))
                || (f.Meeting != null && f.Meeting.ProjectId == projectId && f.Meeting.Status == "COMPLETED"))
            .Select(f => new Activity(f.UploadedBy, "FILE", f.Id, f.OriginalFileName, f.CreatedAt, 0)).ToListAsync(ct));
        var byUser = activities.Where(a => MemberAt(a.UserId, a.OccurredAt)).GroupBy(a => a.UserId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.OccurredAt)
                .ThenBy(a => a.SourceType, StringComparer.Ordinal).ThenBy(a => a.SourceId)
                .Select(a => new ContributionEvidenceDto(a.SourceType, a.SourceId, a.Label, Utc(a.OccurredAt), a.Credit)).ToArray());
        var members = new List<ContributionMemberDto>();
        var evidence = new List<ContributionMemberEvidence>();
        foreach (var member in memberships.GroupBy(m => m.UserId).OrderBy(g => g.Key))
        {
            var items = byUser.GetValueOrDefault(member.Key) ?? [];
            int Count(string type) => items.Count(e => e.SourceType == type);
            members.Add(new(member.Key, member.First().FullName,
                assignments.Count(a => a.UserId == member.Key && MemberAt(member.Key, a.AssignedAt)), Count("TASK"),
                Count("PROGRESS_REPORT"), Count("MEETING"), Count("DELIVERABLE_VERSION"), items.Sum(e => e.Credit), items.Length, Count("FILE")));
            evidence.Add(new(member.Key, items));
        }
        return new(ContributionScoring.RuleVersion, members, evidence);
    }
}
