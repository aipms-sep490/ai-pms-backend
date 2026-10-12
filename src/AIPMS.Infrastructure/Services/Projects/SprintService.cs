using System.Threading.Tasks;
using System.Text.RegularExpressions;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Sprints;
using AIPMS.Application.Features.Sprints.Abstractions;
using AIPMS.Application.Features.Tasks.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Mappers;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class SprintService(AipmsDbContext db, ICurrentUser user, IAuditTrail audit, TimeProvider clock) : ISprintService
{
    private long Actor => user.UserId ?? throw new UnauthorizedException();
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static DateTime? Utc(DateTime? value) => value.HasValue ? Utc(value.Value) : null;
    private sealed record Scope(M.Project Project, bool Manager, bool Whole, long[] Majors);
    private static ValidationException Invalid(string text) => new(new Dictionary<string, string[]> { ["sprint"] = [text] });
    private static void Current(Guid current, string? input)
    { if (!Guid.TryParse(input, out var token) || token != current) throw new ConflictException("The resource changed. Reload before saving."); }
    private static void Page(int page, int size)
    { if (page < 1 || size is < 1 or > 100 || (long)(page - 1) * size > int.MaxValue) throw Invalid("Invalid pagination."); }
    private async Task<Scope> Access(long projectId, bool write, CancellationToken ct)
    {
        var p = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId, ct) ?? throw new NotFoundException("Project", projectId);
        if (write)
        {
            await db.Teams.FromSqlInterpolated($"SELECT * FROM dbo.teams WITH (UPDLOCK,HOLDLOCK) WHERE id={p.TeamId}").AsNoTracking().SingleAsync(ct);
            p = await db.Projects.FromSqlInterpolated($"SELECT * FROM dbo.projects WITH (UPDLOCK,HOLDLOCK) WHERE id={projectId}").AsNoTracking().SingleAsync(ct);
        }
        var account = await db.Users.AsNoTracking().Where(u => u.Id == Actor && u.Status == "ACTIVE" && u.Department != null
            && u.Department.IsActive && u.Department.Organization.IsActive).Select(u => new { u.DepartmentId,
                Staff = u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF"),
                Student = u.UserRoleUsers.Any(r => r.Role.Code == "STUDENT"), Lecturer = u.UserRoleUsers.Any(r => r.Role.Code == "LECTURER") }).SingleOrDefaultAsync(ct)
            ?? throw new ForbiddenException();
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, ct, requireFrozenScope: true);
        if (scope.Provenance != "FROZEN_REGISTRATION_SNAPSHOT") throw new ForbiddenException("A valid frozen project scope is required.");
        var manager = await ProjectExecutionAuthority.IsManagerAsync(db, projectId, Actor, ct);
        var student = account.Student && await db.TeamMembers.AnyAsync(m => m.TeamId == p.TeamId && m.UserId == Actor && m.LeftAt == null, ct);
        var whole = manager || student || account.Staff && account.DepartmentId == scope.LeadDepartmentId;
        var majors = new HashSet<long>();
        if (account.Staff) foreach (var pair in scope.MajorDepartmentIds!) if (pair.Value == account.DepartmentId) majors.Add(pair.Key);
        if (account.Lecturer) majors.UnionWith(await db.SupervisorAssignments.Where(a => a.ProjectId == projectId && a.EndedAt == null
            && a.AssignmentType == "DISCIPLINE_MENTOR" && a.MajorId.HasValue && a.SupervisorProfile.UserId == Actor).Select(a => a.MajorId!.Value).ToArrayAsync(ct));
        majors.IntersectWith(scope.MajorIds);
        if (!whole && majors.Count == 0 || write && !manager) throw new ForbiddenException();
        if (write && p.Status != "ACTIVE") throw new ConflictException("Project is read-only.");
        return new(p, manager, whole, majors.ToArray());
    }
    private SprintDto Dto(Sprint s, Scope scope, bool anotherActive = false) => new(s.Id, s.ProjectId, s.Name, s.Status,
        Utc(s.StartAt), Utc(s.EndAt), Utc(s.StartedAt), Utc(s.CompletedAt), s.ConcurrencyToken.ToString("N"),
        scope.Manager && scope.Project.Status == "ACTIVE" && s.Status == "PLANNED",
        scope.Manager && scope.Project.Status == "ACTIVE" && s.Status == "PLANNED" && !anotherActive && Now < s.EndAt,
        scope.Manager && scope.Project.Status == "ACTIVE" && s.Status == "ACTIVE");
    private async Task<Sprint> Get(long projectId, long id, CancellationToken ct) =>
        await db.Set<Sprint>().SingleOrDefaultAsync(s => s.Id == id && s.ProjectId == projectId, ct) ?? throw new NotFoundException("Sprint", id);
    private Task Audit(string action, long id, object data, CancellationToken ct) => audit.RecordAsync(new(Actor, action, "SPRINT", id,
        new Dictionary<string, object?> { ["change"] = data }), ct);
    public async Task<PagedResult<SprintDto>> List(long projectId, int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize); var scope = await Access(projectId, false, ct);
        var q = db.Set<Sprint>().AsNoTracking().Where(s => s.ProjectId == projectId); var total = await q.LongCountAsync(ct);
        var active = await q.AnyAsync(s => s.Status == "ACTIVE", ct);
        var rows = await q.OrderByDescending(s => s.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        return new(rows.Select(s => Dto(s, scope, active)).ToArray(), page, pageSize, total);
    }
    public async Task<SprintDto> Save(long projectId, long? id, SaveSprintRequest input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 200 || input.EndAt <= input.StartAt || input.EndAt - input.StartAt > TimeSpan.FromDays(90))
            throw Invalid("Name is required (max 200); sprint interval must be positive and at most 90 days.");
        await using var tx = await db.Database.BeginTransactionAsync(ct); var scope = await Access(projectId, true, ct);
        var s = id.HasValue ? await Get(projectId, id.Value, ct) : new Sprint { ProjectId = projectId, CreatedBy = Actor, CreatedAt = Now };
        if (id.HasValue) Current(s.ConcurrencyToken, input.ConcurrencyToken);
        if (s.Status != "PLANNED") throw new ConflictException("Only planned sprints can be edited.");
        s.Name = input.Name.Trim(); s.StartAt = input.StartAt.UtcDateTime; s.EndAt = input.EndAt.UtcDateTime; s.UpdatedAt = Now; s.ConcurrencyToken = Guid.NewGuid();
        if (!id.HasValue) db.Add(s);
        await db.SaveChangesAsync(ct); await Audit(id.HasValue ? "SPRINT_UPDATED" : "SPRINT_CREATED", s.Id, new { projectId }, ct);
        var active = await db.Set<Sprint>().AnyAsync(x => x.ProjectId == projectId && x.Status == "ACTIVE", ct);
        await tx.CommitAsync(ct); return Dto(s, scope, active);
    }
    public async Task<SprintDto> Start(long projectId, long id, SprintTokenRequest input, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); var scope = await Access(projectId, true, ct);
        var s = await Get(projectId, id, ct); Current(s.ConcurrencyToken, input.ConcurrencyToken);
        if (s.Status != "PLANNED" || await db.Set<Sprint>().AnyAsync(x => x.ProjectId == projectId && x.Status == "ACTIVE", ct)) throw new ConflictException("Only one sprint can be active per project.");
        if (Now >= s.EndAt) throw new ConflictException("The planned sprint end has passed.");
        var tasks = await db.Tasks.Where(t => t.SprintId == id).OrderBy(t => t.Id).Take(501).ToArrayAsync(ct);
        if (tasks.Length > 500) throw new ConflictException("A sprint supports at most 500 tasks.");
        s.Status = "ACTIVE"; s.StartedAt = Now; s.UpdatedAt = Now; s.ConcurrencyToken = Guid.NewGuid();
        foreach (var task in tasks) SprintHistory.Record(db, task, id, "BASELINE", true, Actor, s.StartedAt.Value);
        await db.SaveChangesAsync(ct); await Audit("SPRINT_STARTED", id, new { projectId, count = tasks.Length }, ct);
        await tx.CommitAsync(ct); return Dto(s, scope);
    }
    public async Task<SprintDto> Complete(long projectId, long id, CompleteSprintRequest input, CancellationToken ct)
    {
        if (input.Carryover is null || input.Carryover.Count > 500 || input.Carryover.Select(x => x.TaskId).Distinct().Count() != input.Carryover.Count)
            throw Invalid("Provide each unfinished task exactly once (max 500).");
        await using var tx = await db.Database.BeginTransactionAsync(ct); var scope = await Access(projectId, true, ct);
        var s = await Get(projectId, id, ct); Current(s.ConcurrencyToken, input.ConcurrencyToken);
        if (s.Status != "ACTIVE") throw new ConflictException("Only active sprints can be completed.");
        var tasks = await db.Tasks.Where(t => t.SprintId == id && t.Status != "DONE" && t.Status != "CANCELLED").OrderBy(t => t.Id).Take(501).ToArrayAsync(ct);
        if (tasks.Length > 500 || !tasks.Select(t => t.Id).Order().SequenceEqual(input.Carryover.Select(x => x.TaskId).Order())) throw new ConflictException("Carryover must match all unfinished tasks.");
        var targetIds = input.Carryover.Where(x => x.TargetSprintId.HasValue).Select(x => x.TargetSprintId!.Value).Distinct().ToArray();
        var targets = await db.Set<Sprint>().Where(x => targetIds.Contains(x.Id) && x.ProjectId == projectId && x.Status == "PLANNED" && x.Id != id).ToArrayAsync(ct);
        if (targets.Length != targetIds.Length) throw new ConflictException("Carryover targets must be planned sprints in this project.");
        var targetCounts = await db.Tasks.Where(t => t.SprintId.HasValue && targetIds.Contains(t.SprintId.Value))
            .GroupBy(t => t.SprintId!.Value).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        var carryCounts = input.Carryover.Where(x => x.TargetSprintId.HasValue).GroupBy(x => x.TargetSprintId!.Value).ToDictionary(g => g.Key, g => g.Count());
        foreach (var target in targets)
            if (targetCounts.GetValueOrDefault(target.Id) + carryCounts.GetValueOrDefault(target.Id) > 500)
                throw new ConflictException("Carryover exceeds the target sprint task limit.");
        foreach (var t in tasks)
        {
            var move = input.Carryover.Single(x => x.TaskId == t.Id); Current(t.ConcurrencyToken, move.TaskConcurrencyToken);
            SprintHistory.Record(db, t, id, "SCOPE", false, Actor, Now);
            t.SprintId = move.TargetSprintId; t.ConcurrencyToken = Guid.NewGuid(); t.UpdatedAt = Now;
        }
        s.Status = "COMPLETED"; s.CompletedAt = Now; s.UpdatedAt = Now; s.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct); await Audit("SPRINT_COMPLETED", id, new { projectId, carryover = input.Carryover }, ct);
        await tx.CommitAsync(ct); return Dto(s, scope);
    }
    public async Task<TaskDto> SaveTask(long taskId, SaveTaskBoardRequest input, CancellationToken ct)
    {
        if (input.StoryPoints is not (null or 1 or 2 or 3 or 5 or 8 or 13) || input.Labels is null || input.Labels.Count > 10
            || input.Labels.Any(l => l is null || !Regex.IsMatch(l, "^[a-z0-9][a-z0-9-]{0,31}$")) || input.Labels.Distinct().Count() != input.Labels.Count)
            throw Invalid("Use Fibonacci points 1/2/3/5/8/13 (or null) and up to 10 unique lowercase ASCII labels.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var projectId = await db.Tasks.Where(t => t.Id == taskId).Select(t => (long?)t.Milestone.ProjectId).SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Task", taskId);
        await Access(projectId, true, ct);
        var task = await db.Tasks.Include(t => t.BoardLabels).SingleAsync(t => t.Id == taskId, ct); Current(task.ConcurrencyToken, input.ConcurrencyToken);
        var oldId = task.SprintId; var now = Now;
        var old = oldId.HasValue ? await Get(projectId, oldId.Value, ct) : null;
        if (old?.Status == "COMPLETED" || task.Status == "CANCELLED") throw new ConflictException("Completed sprint scope and cancelled tasks are read-only.");
        var target = input.SprintId.HasValue ? await Get(projectId, input.SprintId.Value, ct) : null;
        if (target?.Status == "COMPLETED") throw new ConflictException("Cannot add tasks to a completed sprint.");
        if (target is not null && oldId != target.Id && await db.Tasks.CountAsync(t => t.SprintId == target.Id, ct) >= 500) throw new ConflictException("A sprint supports at most 500 tasks.");
        if (old?.Status == "ACTIVE" && oldId != input.SprintId) SprintHistory.Record(db, task, old.Id, "SCOPE", false, Actor, now);
        var estimateChanged = task.StoryPoints != input.StoryPoints;
        task.SprintId = input.SprintId; task.StoryPoints = input.StoryPoints; task.UpdatedAt = now; task.ConcurrencyToken = Guid.NewGuid();
        if (target?.Status == "ACTIVE" && (oldId != target.Id || estimateChanged)) SprintHistory.Record(db, task, target.Id, oldId == target.Id ? "ESTIMATE" : "SCOPE", true, Actor, now);
        db.RemoveRange(task.BoardLabels.Where(l => !input.Labels.Contains(l.Label)));
        foreach (var label in input.Labels.Where(l => task.BoardLabels.All(x => x.Label != l))) task.BoardLabels.Add(new() { Label = label });
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new(Actor, "TASK_BOARD_UPDATED", "TASK", taskId,
            new Dictionary<string, object?> { ["projectId"] = projectId, ["sprintId"] = task.SprintId, ["storyPoints"] = task.StoryPoints, ["labels"] = input.Labels }), ct);
        var dto = await TaskDto(taskId, ct); await tx.CommitAsync(ct); return dto;
    }
    private async Task<TaskDto> TaskDto(long id, CancellationToken ct) => (await TaskQuery().SingleAsync(t => t.Id == id, ct)).ToDto();
    private IQueryable<M.Task> TaskQuery() => db.Tasks.AsNoTracking().Include(t => t.BoardLabels).Include(t => t.CreatedByNavigation)
        .Include(t => t.TaskAssignees).ThenInclude(a => a.User).Include(t => t.TaskDependencyTasks);
    public async Task<PagedResult<TaskDto>> Board(long projectId, long? sprintId, bool backlog, string? label, long? majorId, string? status, int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize); if (backlog && sprintId.HasValue || label?.Length > 32) throw Invalid("Invalid board filters.");
        var scope = await Access(projectId, false, ct);
        if (sprintId.HasValue) await Get(projectId, sprintId.Value, ct);
        if (majorId.HasValue && !(await ProjectAcademicScopeReader.ReadAsync(db, projectId, ct, true)).MajorIds.Contains(majorId.Value)) throw new ForbiddenException();
        var majors = scope.Majors;
        var q = TaskQuery().Where(t => t.Milestone.ProjectId == projectId && (scope.Whole || db.TaskDisciplines.Any(d => d.TaskId == t.Id && majors.Contains(d.MajorId))));
        if (sprintId.HasValue) q = q.Where(t => t.SprintId == sprintId);
        if (backlog) q = q.Where(t => t.SprintId == null);
        if (label is not null) q = q.Where(t => t.BoardLabels.Any(l => l.Label == label));
        if (majorId.HasValue) q = q.Where(t => db.TaskDisciplines.Any(d => d.TaskId == t.Id && d.MajorId == majorId));
        if (status is not null) q = q.Where(t => t.Status == status);
        var count = await q.LongCountAsync(ct); var rows = await q.OrderBy(t => t.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        return new(rows.Select(t => t.ToDto()).ToArray(), page, pageSize, count);
    }
    public async Task<SprintBurndownDto> Burndown(long projectId, long id, CancellationToken ct)
    {
        var scope = await Access(projectId, false, ct);
        if (!scope.Whole) throw new ForbiddenException("Whole-project history requires whole-project authority.");
        var sprint = await Get(projectId, id, ct);
        var events = await db.Set<SprintTaskEvent>().AsNoTracking().Where(e => e.SprintId == id).OrderBy(e => e.Id).Take(10001).ToArrayAsync(ct);
        if (events.Length > 10000) throw Invalid("History exceeds the synchronous 10000-event limit.");
        var states = new Dictionary<long, SprintTaskEvent>(); var points = new List<SprintBurndownPoint>();
        // Fold immutable events, never current task estimates/status, to preserve completed charts.
        foreach (var batch in events.GroupBy(e => e.OccurredAt))
        {
            foreach (var e in batch) { if (e.InScope) states[e.TaskId] = e; else states.Remove(e.TaskId); }
            points.Add(new(Utc(batch.Key), states.Values.Sum(e => e.StoryPoints ?? 0), states.Values.Where(e => e.TaskStatus is not ("DONE" or "CANCELLED")).Sum(e => e.StoryPoints ?? 0),
                states.Values.Count(e => e.StoryPoints is null)));
        }
        return new(id, Utc(sprint.StartedAt), points, true);
    }
}
