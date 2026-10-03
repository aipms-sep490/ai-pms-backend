using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Calendar.Abstractions;
using AIPMS.Application.Features.Calendar.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class CalendarService(AipmsDbContext db, ICurrentUser currentUser) : ICalendarService
{
    private sealed record Cursor(DateTime DueAt, long Id);
    private sealed record AccessScope(bool IsAdmin, IReadOnlySet<long> ProjectIds, IReadOnlySet<long> MentorProjectIds, IReadOnlySet<long> EvaluatorProjectIds);

    public async Task<CalendarResponseDto> GetAsync(DateTimeOffset from, DateTimeOffset to, string? cursor, int pageSize, string? sourceType, CancellationToken ct = default)
    {
        if (from >= to || to - from > TimeSpan.FromDays(366)) throw new ValidationException(new Dictionary<string, string[]> { ["range"] = ["from/to must be a bounded range of at most 366 days."] });
        if (pageSize is < 1 or > 100) throw new ValidationException(new Dictionary<string, string[]> { ["pageSize"] = ["pageSize must be between 1 and 100."] });
        var allowedSources = new[] { "TASK", "MILESTONE", "MEETING", "DELIVERABLE", "FINAL_SUBMISSION" };
        if (sourceType is not null && !allowedSources.Contains(sourceType, StringComparer.OrdinalIgnoreCase)) throw new ValidationException(new Dictionary<string, string[]> { ["sourceType"] = ["Unknown sourceType."] });
        var actor = currentUser.UserId ?? throw new UnauthorizedException();
        sourceType = sourceType?.Trim().ToUpperInvariant();
        var scope = await Scope(actor, ct);
        if (scope.IsAdmin) return new([], null, false, true);
        Cursor? after = Decode(cursor);
        var items = new List<CalendarItemDto>();
        if (sourceType is null or "TASK") items.AddRange(await Tasks(scope, from, to, ct));
        if (sourceType is null or "MILESTONE") items.AddRange(await Milestones(scope, from, to, ct));
        if (sourceType is null or "MEETING") items.AddRange(await Meetings(scope, from, to, ct));
        if (sourceType is null or "DELIVERABLE") items.AddRange(await Deliverables(scope, from, to, ct));
        if (sourceType is null or "FINAL_SUBMISSION") items.AddRange(await Finals(scope, from, to, ct));
        var ordered = items.Where(x => after is null || x.DueAt > after.DueAt || x.DueAt == after.DueAt && x.SourceId > after.Id)
            .OrderBy(x => x.DueAt).ThenBy(x => x.SourceId).ThenBy(x => x.SourceType, StringComparer.Ordinal).ToArray();
        var page = ordered.Take(pageSize).ToArray();
        var hasMore = ordered.Length > page.Length;
        var next = hasMore && page.Length > 0 ? Encode(new Cursor(page[^1].DueAt, page[^1].SourceId)) : null;
        return new(page, next, hasMore, true);
    }

    private async Task<AccessScope> Scope(long actor, CancellationToken ct)
    {
        var account = await db.Users.AsNoTracking().Where(u => u.Id == actor && u.Status == "ACTIVE")
            .Select(u => new { u.DepartmentId, Admin = u.UserRoleUsers.Any(r => r.Role.Code == "ADMIN"), Staff = u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF"), Student = u.UserRoleUsers.Any(r => r.Role.Code == "STUDENT"), Lecturer = u.UserRoleUsers.Any(r => r.Role.Code == "LECTURER") }).SingleOrDefaultAsync(ct)
            ?? throw new ForbiddenException("An active account is required.");
        if (account.Admin) return new(true, new HashSet<long>(), new HashSet<long>(), new HashSet<long>());
        var projects = new HashSet<long>();
        if (account.Student) foreach (var id in await db.TeamMembers.Where(m => m.UserId == actor && m.LeftAt == null).Select(m => m.Team.Project!.Id).ToListAsync(ct)) projects.Add(id);
        if (account.Lecturer) foreach (var id in await db.SupervisorAssignments.Where(a => a.SupervisorProfile.UserId == actor && a.EndedAt == null).Select(a => a.ProjectId).ToListAsync(ct)) projects.Add(id);
        if (account.Staff && account.DepartmentId is long dept) foreach (var id in await db.ProjectMajors.Where(m => m.Major.DepartmentId == dept).Select(m => m.ProjectId).Distinct().ToListAsync(ct)) projects.Add(id);
        var mentors = account.Lecturer ? await db.SupervisorAssignments.Where(a => a.SupervisorProfile.UserId == actor && a.EndedAt == null && a.AssignmentType == "DISCIPLINE_MENTOR").Select(a => a.ProjectId).Distinct().ToListAsync(ct) : [];
        var evals = account.Lecturer ? await db.Set<AIPMS.Infrastructure.Persistence.Models.EvaluationAssignment>().Where(a => a.EvaluatorId == actor && a.Status == "ACTIVE").Select(a => a.ProjectId).Distinct().ToListAsync(ct) : [];
        foreach (var id in mentors) projects.Add(id); foreach (var id in evals) projects.Add(id);
        return new(false, projects, mentors.ToHashSet(), evals.ToHashSet());
    }

    private async Task<IReadOnlyList<CalendarItemDto>> Tasks(AccessScope scope, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        await db.Tasks.AsNoTracking().Where(t => scope.ProjectIds.Contains(t.Milestone.ProjectId) && t.DueAt.HasValue && t.DueAt >= from.UtcDateTime && t.DueAt < to.UtcDateTime)
            .Select(t => new CalendarItemDto("TASK", t.Id, t.Milestone.ProjectId, t.Milestone.Project.Code, t.Milestone.Project.Title, t.Title, t.StartAt, t.DueAt, t.DueAt!.Value, t.Status, $"/projects/{t.Milestone.ProjectId}/tasks/{t.Id}" )).ToListAsync(ct);
    private async Task<IReadOnlyList<CalendarItemDto>> Milestones(AccessScope scope, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        await db.Milestones.AsNoTracking().Where(m => scope.ProjectIds.Contains(m.ProjectId) && m.DueDate.HasValue && m.DueDate >= DateOnly.FromDateTime(from.UtcDateTime) && m.DueDate < DateOnly.FromDateTime(to.UtcDateTime))
            .Select(m => new CalendarItemDto("MILESTONE", m.Id, m.ProjectId, m.Project.Code, m.Project.Title, m.Title, null, null, m.DueDate!.Value.ToDateTime(TimeOnly.MinValue), m.Status, $"/projects/{m.ProjectId}/milestones/{m.Id}" )).ToListAsync(ct);
    private async Task<IReadOnlyList<CalendarItemDto>> Meetings(AccessScope scope, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        await db.Meetings.AsNoTracking().Where(m => scope.ProjectIds.Contains(m.ProjectId) && m.StartAt >= from.UtcDateTime && m.StartAt < to.UtcDateTime)
            .Select(m => new CalendarItemDto("MEETING", m.Id, m.ProjectId, m.Project.Code, m.Project.Title, m.Title, m.StartAt, m.EndAt, m.StartAt, m.Status, $"/projects/{m.ProjectId}/meetings/{m.Id}" )).ToListAsync(ct);
    private async Task<IReadOnlyList<CalendarItemDto>> Deliverables(AccessScope scope, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        await db.Deliverables.AsNoTracking().Where(d => scope.ProjectIds.Contains(d.ProjectId) && d.DueAt.HasValue && d.DueAt >= from.UtcDateTime && d.DueAt < to.UtcDateTime)
            .Select(d => new CalendarItemDto("DELIVERABLE", d.Id, d.ProjectId, d.Project.Code, d.Project.Title, d.Title, null, null, d.DueAt!.Value, d.Status, $"/projects/{d.ProjectId}/deliverables/{d.Id}" )).ToListAsync(ct);
    private async Task<IReadOnlyList<CalendarItemDto>> Finals(AccessScope scope, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        return await db.Set<AIPMS.Infrastructure.Persistence.Models.FinalSubmission>().AsNoTracking()
            .Join(db.Projects.AsNoTracking(), f => f.ProjectId, p => p.Id, (f, p) => new { f, p })
            .Where(x => scope.ProjectIds.Contains(x.f.ProjectId) && x.f.Deadline >= from.UtcDateTime && x.f.Deadline < to.UtcDateTime)
            .Select(x => new CalendarItemDto("FINAL_SUBMISSION", x.f.Id, x.f.ProjectId, x.p.Code, x.p.Title, "Final submission", null, null, x.f.Deadline, "LOCKED", $"/projects/{x.f.ProjectId}/final-submission" ))
            .ToListAsync(ct);
    }
    private static string Encode(Cursor c) => Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(c)));
    private static Cursor? Decode(string? value) { if (string.IsNullOrWhiteSpace(value)) return null; try { return JsonSerializer.Deserialize<Cursor>(Encoding.UTF8.GetString(Convert.FromBase64String(value))); } catch { throw new ValidationException(new Dictionary<string, string[]> { ["cursor"] = ["Invalid cursor."] }); } }
}
