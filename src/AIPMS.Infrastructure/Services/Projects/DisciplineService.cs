using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Deliverables.Abstractions;
using AIPMS.Application.Features.Deliverables.Services;
using AIPMS.Application.Features.Disciplines.Abstractions;
using AIPMS.Application.Features.Disciplines.DTOs;
using AIPMS.Application.Features.Disciplines.Validators;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Projects.Validators;
using AIPMS.Application.Features.Tasks.DTOs;
using AIPMS.Application.Features.Teams.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;
using ProjectEvidence = AIPMS.Infrastructure.Persistence.Generated.Models.ProjectEvidence;
using TaskDiscipline = AIPMS.Infrastructure.Persistence.Generated.Models.TaskDiscipline;
using TeamMajorResponsibility = AIPMS.Infrastructure.Persistence.Generated.Models.TeamMajorResponsibility;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class DisciplineService(AipmsDbContext db, ICurrentUser current, IProjectAccessService access,
    ITeamEligibilityAccessService teamAccess, IProjectRepository projects, IDeliverableRepository files,
    DeliverableWorkflow fileWorkflow, IAuditTrail audit, TimeProvider clock, IProjectScopeResolver? scopeResolver = null) : IDisciplineService
{
    private long Actor => current.UserId ?? throw new UnauthorizedException();
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public Task<ResponsibilityListDto> TeamResponsibilitiesAsync(long teamId, long majorId, CancellationToken ct) =>
        projects.InTransactionAsync<ResponsibilityListDto>(async token =>
        {
            await teamAccess.ValidateCanReadEligibilityAsync(teamId, token);
            var config = await db.Set<TeamAcademicConfiguration>().AsNoTracking().SingleOrDefaultAsync(x => x.TeamId == teamId, token)
                ?? throw new NotFoundException("Team academic configuration", teamId);
            if (!await db.Set<TeamMajorRequirement>().AnyAsync(x => x.TeamId == teamId && x.MajorId == majorId, token))
                throw new NotFoundException("Team major requirement", majorId);
            return new(config.ConcurrencyToken.ToString("N"), false, true, await ResponsibilityRows(teamId, majorId, token));
        }, ct);

    private async Task<IReadOnlyList<ResponsibilityDto>> ResponsibilityRows(long teamId, long? majorId, CancellationToken ct) =>
        await db.TeamMajorResponsibilities.AsNoTracking().Where(x => x.TeamId == teamId && (majorId == null || x.MajorId == majorId))
            .OrderBy(x => x.MajorId).ThenBy(x => x.SortOrder).Select(x => new ResponsibilityDto(x.Id, x.MajorId, x.Content, x.SortOrder, x.ConcurrencyToken.ToString("N"))).ToArrayAsync(ct);

    public Task<ResponsibilityListDto> ProjectResponsibilitiesAsync(long projectId, long majorId, CancellationToken ct) =>
        projects.InTransactionAsync(token => ProjectResponsibilitiesCore(projectId, majorId, token), ct);

    private async Task<ResponsibilityListDto> ProjectResponsibilitiesCore(long projectId, long majorId, CancellationToken ct)
    {
        await RequireAccess(projectId, ct);
        var project = await db.Projects.AsNoTracking().SingleAsync(x => x.Id == projectId, ct);
        var scope = await Scope(projectId, ct);
        if (!scope.Majors.Contains(majorId)) throw new NotFoundException("Project major", majorId);
        if (project.Status is "DRAFT" or "REVISION_REQUIRED")
            return new(null, false, true, await ResponsibilityRows(project.TeamId, majorId, ct));
        var json = await db.Set<ProjectRegistrationSnapshot>().AsNoTracking().Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.Id).Select(x => x.SnapshotJson).FirstOrDefaultAsync(ct);
        var items = json is null ? null : JsonSerializer.Deserialize<RegistrationEvidence>(json)?.TeamResponsibilities;
        return new(null, true, items is not null, items?.Where(x => x.MajorId == majorId).ToArray() ?? []);
    }

    public Task<ResponsibilityListDto> ReplaceResponsibilitiesAsync(long teamId, long majorId, ReplaceResponsibilitiesRequest request, CancellationToken ct) =>
        projects.InTransactionAsync<ResponsibilityListDto>(async token =>
        {
            var team = await LockTeam(teamId, token);
            await teamAccess.ValidateCanReadEligibilityAsync(teamId, token);
            var teamProjects = await db.Projects.Where(x => x.TeamId == teamId).OrderBy(x => x.Id).ToListAsync(token);
            foreach (var project in teamProjects)
                await db.Projects.FromSqlInterpolated($"SELECT * FROM dbo.projects WITH (UPDLOCK,HOLDLOCK) WHERE id={project.Id}").SingleAsync(token);
            if (team.Status is "LOCKED" or "DISBANDED" || teamProjects.Any(x => x.Status is not ("DRAFT" or "REVISION_REQUIRED" or "REJECTED" or "ARCHIVED")))
                throw new ConflictException("Responsibilities are locked for this team.");
            var leader = await IsLeader(teamId, token);
            var supervisor = !leader && await db.SupervisorAssignments.AnyAsync(x => x.Project.TeamId == teamId
                && (x.Project.Status == "DRAFT" || x.Project.Status == "REVISION_REQUIRED")
                && x.EndedAt == null && x.SupervisorProfile.UserId == Actor
                && x.SupervisorProfile.User.UserRoleUsers.Any(r => r.Role.Code == "LECTURER")
                && (x.AssignmentType == "PRIMARY" || x.AssignmentType == "DISCIPLINE_MENTOR" && x.MajorId == majorId), token);
            if (!leader && !supervisor) throw new ForbiddenException("Only the active team leader or scoped assigned supervisor can edit responsibilities.");
            var config = await db.Set<TeamAcademicConfiguration>().SingleOrDefaultAsync(x => x.TeamId == teamId, token)
                ?? throw new NotFoundException("Team academic configuration", teamId);
            CheckToken(config.ConcurrencyToken, request.ConcurrencyToken);
            if (!await db.Set<TeamMajorRequirement>().AnyAsync(x => x.TeamId == teamId && x.MajorId == majorId, token))
                throw new NotFoundException("Team major requirement", majorId);
            DisciplineValidation.Responsibilities(request.Items);
            db.TeamMajorResponsibilities.RemoveRange(await db.TeamMajorResponsibilities.Where(x => x.TeamId == teamId && x.MajorId == majorId).ToListAsync(token));
            await db.SaveChangesAsync(token);
            db.TeamMajorResponsibilities.AddRange(request.Items.Select(x => new TeamMajorResponsibility { TeamId = teamId, MajorId = majorId,
                Content = x.Content.Trim(), SortOrder = x.SortOrder, ConcurrencyToken = Guid.NewGuid(), CreatedBy = Actor, CreatedAt = Now }));
            config.ConcurrencyToken = Guid.NewGuid(); config.ResponsibilityVersion = Guid.NewGuid();
            foreach (var project in teamProjects.Where(x => x.Status is "DRAFT" or "REVISION_REQUIRED"))
            { project.UpdatedAt = Now; db.Entry(project).Property(x => x.UpdatedAt).IsModified = true; }
            await db.SaveChangesAsync(token);
            await Audit("TEAM_MAJOR_RESPONSIBILITIES_REPLACED", "TEAM", teamId, new { majorId, count = request.Items.Count }, token);
            await Audit("TEAM_ELIGIBILITY_INVALIDATED", "TEAM", teamId, new { reason = "STRUCTURED_RESPONSIBILITIES_CHANGED" }, token);
            return new(config.ConcurrencyToken.ToString("N"), false, true, await ResponsibilityRows(teamId, majorId, token));
        }, ct);

    public Task<TaskDisciplinesDto> TaskDisciplinesAsync(long taskId, CancellationToken ct) =>
        projects.InTransactionAsync<TaskDisciplinesDto>(async token =>
        {
            var projectId = await TaskProject(taskId, token); await RequireAccess(projectId, token);
            var task = await db.Tasks.AsNoTracking().SingleAsync(x => x.Id == taskId, token);
            var rows = await db.TaskDisciplines.AsNoTracking().Where(x => x.TaskId == taskId).OrderBy(x => x.MajorId)
                .Select(x => new TaskDisciplineInput(x.MajorId, x.Role)).ToArrayAsync(token);
            return new(task.ConcurrencyToken.ToString("N"), rows.Length == 0 ? "UNCLASSIFIED" : "CLASSIFIED", rows);
        }, ct);

    public Task<TaskDisciplinesDto> ReplaceTaskDisciplinesAsync(long taskId, ReplaceTaskDisciplinesRequest request, CancellationToken ct) =>
        projects.InTransactionAsync<TaskDisciplinesDto>(async token =>
        {
            var projectId = await TaskProject(taskId, token); await LockProject(projectId, token);
            var task = await db.Tasks.FromSqlInterpolated($"SELECT * FROM dbo.tasks WITH (UPDLOCK,HOLDLOCK) WHERE id={taskId}").SingleAsync(token);
            var scope = await Scope(projectId, token);
            DisciplineValidation.Disciplines(request.Items, scope.Interdisciplinary);
            if (request.Items.Any(x => !scope.Majors.Contains(x.MajorId))) throw new ConflictException("Task discipline is outside project requirements.");
            var old = await db.TaskDisciplines.Where(x => x.TaskId == taskId).ToListAsync(token);
            await RequireWriter(projectId, old.Select(x => x.MajorId).Concat(request.Items.Select(x => x.MajorId)).Distinct().ToArray(), taskId, token);
            CheckToken(task.ConcurrencyToken, request.ConcurrencyToken);
            if (task.Status is "CANCELLED") throw new ConflictException("Cancelled tasks are read-only.");
            db.TaskDisciplines.RemoveRange(old); await db.SaveChangesAsync(token);
            await SaveDisciplines(taskId, request.Items, token);
            task.ConcurrencyToken = Guid.NewGuid(); task.UpdatedAt = Now;
            await db.SaveChangesAsync(token);
            await Audit("TASK_DISCIPLINES_REPLACED", "TASK", taskId, request.Items, token);
            return new(task.ConcurrencyToken.ToString("N"), request.Items.Count == 0 ? "UNCLASSIFIED" : "CLASSIFIED", request.Items.OrderBy(x => x.MajorId).ToArray());
        }, ct);

    public Task<TaskDto> CreateTaskAsync(long milestoneId, IReadOnlyList<TaskDisciplineInput>? disciplines, Func<CancellationToken, Task<TaskDto>> create, CancellationToken ct) =>
        projects.InTransactionAsync(async token =>
        {
            var projectId = await db.Milestones.Where(x => x.Id == milestoneId).Select(x => (long?)x.ProjectId).SingleOrDefaultAsync(token)
                ?? throw new NotFoundException("Milestone", milestoneId);
            await LockProject(projectId, token);
            var scope = await Scope(projectId, token); var items = disciplines ?? [];
            DisciplineValidation.Disciplines(items, scope.Interdisciplinary);
            if (items.Any(x => !scope.Majors.Contains(x.MajorId))) throw new ConflictException("Task discipline is outside project requirements.");
            await RequireWriter(projectId, items.Select(x => x.MajorId).ToArray(), null, token, allowSelfScopedMemberCreate: true);
            var result = await create(token);
            await SaveDisciplines(result.Id, items, token);
            if (items.Count > 0) await Audit("TASK_DISCIPLINES_REPLACED", "TASK", result.Id, items, token);
            return result;
        }, ct);

    private async Task SaveDisciplines(long taskId, IReadOnlyList<TaskDisciplineInput> items, CancellationToken ct)
    {
        db.TaskDisciplines.AddRange(items.Select(x => new TaskDiscipline { TaskId = taskId, MajorId = x.MajorId, Role = x.Role, CreatedBy = Actor, CreatedAt = Now }));
        await db.SaveChangesAsync(ct);
    }

    public Task<ProjectEvidenceDto> AddEvidenceAsync(long projectId, CreateProjectEvidenceRequest request, CancellationToken ct) =>
        projects.InTransactionAsync(async token =>
        {
            await LockProject(projectId, token); DisciplineValidation.Evidence(request);
            var scope = await Scope(projectId, token);
            if (request.MajorId is long major && !scope.Majors.Contains(major)) throw new ConflictException("Evidence major is outside project requirements.");
            var source = await ResolveSource(request.SourceType, request.SourceId, token);
            if (source.ProjectId != projectId) throw new NotFoundException("Evidence source", request.SourceId);
            await RequireWriter(projectId, request.MajorId is long id ? [id] : [], source.TaskId, token);
            if (source.Status is "CANCELLED" or "CLOSED" or "LOCKED" or "ARCHIVED") throw new ConflictException("The evidence source is read-only.");
            if (source.TaskId is long taskId && request.MajorId is long taskMajor
                && !await db.TaskDisciplines.AnyAsync(x => x.TaskId == taskId && x.MajorId == taskMajor, token))
                throw new ConflictException("Classified task evidence must match a task discipline.");
            var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
            var existing = await db.ProjectEvidence.SingleOrDefaultAsync(x => x.ProjectId == projectId && x.SourceType == request.SourceType
                && x.SourceId == request.SourceId && x.MajorId == request.MajorId, token);
            if (existing is not null)
            {
                if (existing.Notes != notes) throw new ConflictException("This source/major is already recorded with different metadata.");
                return (await MapEvidenceAsync([(existing, existing.VerificationStatus)], token))[0];
            }
            var row = new ProjectEvidence { ProjectId = projectId, MajorId = request.MajorId, SourceType = request.SourceType,
                SourceId = request.SourceId, Notes = notes, SubmittedBy = Actor, SubmittedAt = Now, VerificationStatus = "PENDING",
                TaskId = request.SourceType == "TASK" ? request.SourceId : null, DeliverableId = request.SourceType == "DELIVERABLE" ? request.SourceId : null,
                MeetingId = request.SourceType == "MEETING" ? request.SourceId : null, ProgressReportId = request.SourceType == "PROGRESS_REPORT" ? request.SourceId : null,
                FileId = request.SourceType == "FILE" ? request.SourceId : null };
            db.ProjectEvidence.Add(row); await db.SaveChangesAsync(token);
            var dto = (await MapEvidenceAsync([(row, row.VerificationStatus)], token))[0];
            await Audit("PROJECT_EVIDENCE_CREATED", "PROJECT_EVIDENCE", row.Id, dto, token);
            return dto;
        }, ct);

    public async Task<PagedResult<ProjectEvidenceDto>> EvidenceAsync(long projectId, string? sourceType, long? majorId, string? verificationStatus, int page, int pageSize, CancellationToken ct)
    {
        await RequireAccess(projectId, ct); ProjectRequirementsValidation.ValidatePage(page, pageSize);
        if (sourceType is not null) DisciplineValidation.Source(sourceType);
        if (majorId is <= 0 || verificationStatus is not (null or "PENDING" or "UNKNOWN" or "VERIFIED" or "REJECTED")) DisciplineValidation.Invalid("filter", "Invalid evidence filter.");
        // TK-W2-05: Discipline Mentor only sees COMMON/unclassified evidence (MajorId == null) and their assigned MajorId(s).
        long[]? mentorAllowedMajors = null;
        if (scopeResolver is not null)
        {
            var actorScope = await scopeResolver.ResolveAsync(Actor, projectId, ct);
            if (actorScope is { IsFullProjectAccess: false, IsMentor: true })
            {
                if (majorId.HasValue && !actorScope.CanAccessMajor(majorId.Value))
                    throw new ForbiddenException("The requested major is outside your assigned mentor scope.", WorkflowErrorCodes.ScopeAccessDenied);
                mentorAllowedMajors = actorScope.AllowedMajorIds.ToArray();
            }
        }
        // Legacy rows may still carry PENDING after a review; project the latest decision at read time.
        var query = db.ProjectEvidence.AsNoTracking()
            .Where(x => x.ProjectId == projectId
                && (sourceType == null || x.SourceType == sourceType)
                && (majorId == null || x.MajorId == majorId)
                && (mentorAllowedMajors == null || x.MajorId == null || mentorAllowedMajors.Contains(x.MajorId.Value)))
            .Select(x => new
            {
                Evidence = x,
                LatestDecision = db.Set<ProjectEvidenceReview>()
                    .Where(r => r.EvidenceId == x.Id)
                    .OrderByDescending(r => r.Id)
                    .Select(r => r.Decision)
                    .FirstOrDefault()
            })
            .Select(x => new
            {
                x.Evidence,
                EffectiveStatus = x.LatestDecision == "ACCEPTED" ? "VERIFIED"
                    : x.LatestDecision == "REJECTED" ? "REJECTED"
                    : x.LatestDecision == "CHANGES_REQUESTED" ? "PENDING"
                    : x.Evidence.VerificationStatus
            });
        if (verificationStatus is not null) query = query.Where(x => x.EffectiveStatus == verificationStatus);
        var count = await query.LongCountAsync(ct);
        var rows = await query.OrderByDescending(x => x.Evidence.SubmittedAt).ThenByDescending(x => x.Evidence.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        // A ledger reference never substitutes for file metadata/download authorization.
        // TK-W2-03: Never crash the whole ledger page with 403 when a single cross-scope FILE entry is inaccessible; filter it out safely.
        var accessibleRows = new List<(AIPMS.Infrastructure.Persistence.Generated.Models.ProjectEvidence Evidence, string EffectiveStatus)>(rows.Count);
        foreach (var row in rows)
        {
            if (row.Evidence.FileId.HasValue)
            {
                try
                {
                    await fileWorkflow.FileAsync(row.Evidence.FileId.Value, ct);
                }
                catch (ForbiddenException)
                {
                    continue;
                }
                catch (NotFoundException)
                {
                    continue;
                }
            }
            accessibleRows.Add((row.Evidence, row.EffectiveStatus));
        }
        return new(await MapEvidenceAsync(accessibleRows, ct), page, pageSize, count);
    }

    private sealed record Source(long ProjectId, string Status, long? TaskId = null);
    private async Task<Source> ResolveSource(string type, long id, CancellationToken ct)
    {
        if (type == "FILE")
        {
            var stored = await files.GetFileAsync(id, ct) ?? throw new NotFoundException("File", id);
            await fileWorkflow.FileAsync(id, ct);
            var parent = await files.GetParentAsync(stored.Dto.ParentType, stored.Dto.ParentId, ct) ?? throw new NotFoundException("File parent", id);
            var taskStatus = parent.Type == "TASK" ? await db.Tasks.Where(x => x.Id == parent.Id).Select(x => x.Status).SingleAsync(ct) : parent.Status;
            return new(stored.ProjectId, taskStatus, parent.Type == "TASK" ? parent.Id : null);
        }
        return await (type switch
        {
            "TASK" => db.Tasks.Where(x => x.Id == id).Select(x => new Source(x.Milestone.ProjectId, x.Status, x.Id)),
            "DELIVERABLE" => db.Deliverables.Where(x => x.Id == id).Select(x => new Source(x.ProjectId, x.Status, null)),
            "MEETING" => db.Meetings.Where(x => x.Id == id).Select(x => new Source(x.ProjectId, x.Status, null)),
            "PROGRESS_REPORT" => db.ProgressReports.Where(x => x.Id == id).Select(x => new Source(x.ProjectId, x.Status, null)),
            _ => throw new ConflictException("Unknown evidence source.")
        }).SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Evidence source", id);
    }

    private async Task<(IReadOnlyList<long> Majors, bool Interdisciplinary)> Scope(long projectId, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking().SingleAsync(x => x.Id == projectId, ct);
        if (project.Status is not ("DRAFT" or "REVISION_REQUIRED"))
        {
            var json = await db.Set<ProjectRegistrationSnapshot>().Where(x => x.ProjectId == projectId).OrderByDescending(x => x.Id).Select(x => x.SnapshotJson).FirstOrDefaultAsync(ct);
            if (json is not null)
            {
                var e = JsonSerializer.Deserialize<RegistrationEvidence>(json)!;
                return (e.ProjectRequirements is { Count: > 0 } ? e.ProjectRequirements.Select(x => x.MajorId).ToArray() : e.Scope.Requirements.Select(x => x.MajorId).ToArray(),
                    e.Scope.ProjectMode == "INTERDISCIPLINARY");
            }
        }
        var explicitMajors = await db.ProjectMajorRequirements.Where(x => x.ProjectId == projectId).Select(x => x.MajorId).ToArrayAsync(ct);
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, ct);
        return (explicitMajors.Length > 0 ? explicitMajors : scope.MajorIds, scope.MajorIds.Count > 1);
    }

    private async Task RequireWriter(long projectId, IReadOnlyList<long> majorIds, long? taskId, CancellationToken ct, bool allowSelfScopedMemberCreate = false)
    {
        await RequireAccess(projectId, ct);
        var teamId = await db.Projects.Where(x => x.Id == projectId).Select(x => x.TeamId).SingleAsync(ct);
        if (await IsLeader(teamId, ct)) return;
        var assignments = await db.SupervisorAssignments.Where(x => x.ProjectId == projectId && x.EndedAt == null && x.SupervisorProfile.UserId == Actor
            && x.SupervisorProfile.User.UserRoleUsers.Any(r => r.Role.Code == "LECTURER")).Select(x => new { x.AssignmentType, x.MajorId }).ToListAsync(ct);
        if (assignments.Any(x => x.AssignmentType == "PRIMARY")) return;
        if (majorIds.Count > 0 && majorIds.All(id => assignments.Any(x => x.AssignmentType == "DISCIPLINE_MENTOR" && x.MajorId == id))) return;
        if (taskId is long task && await db.TaskAssignees.AnyAsync(x => x.TaskId == task && x.UserId == Actor
            && x.User.UserRoleUsers.Any(r => r.Role.Code == "STUDENT")
            && db.TeamMembers.Any(m => m.TeamId == teamId && m.UserId == Actor && m.LeftAt == null), ct)) return;
        if (allowSelfScopedMemberCreate && taskId is null)
        {
            var activeMember = await db.TeamMembers.AsNoTracking()
                .Where(m => m.TeamId == teamId && m.UserId == Actor && m.LeftAt == null
                    && m.User.Status == "ACTIVE" && m.User.UserRoleUsers.Any(r => r.Role.Code == "STUDENT"))
                .Select(m => new { m.User.MajorId })
                .FirstOrDefaultAsync(ct);
            if (activeMember is not null && (majorIds.Count == 0 || (activeMember.MajorId.HasValue && majorIds.All(id => id == activeMember.MajorId.Value))))
                return;
        }
        throw new ForbiddenException("Only the active leader, scoped assigned supervisor, or member assigned to this task may write.");
    }

    private Task<bool> IsLeader(long teamId, CancellationToken ct) => db.TeamMembers.AnyAsync(x => x.TeamId == teamId && x.UserId == Actor
        && x.LeftAt == null && x.IsLeader && x.User.Status == "ACTIVE" && x.User.UserRoleUsers.Any(r => r.Role.Code == "STUDENT"), ct);

    private async Task<M.Team> LockTeam(long teamId, CancellationToken ct) =>
        await db.Teams.FromSqlInterpolated($"SELECT * FROM dbo.teams WITH (UPDLOCK,HOLDLOCK) WHERE id={teamId}").SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Team", teamId);

    private async Task LockProject(long projectId, CancellationToken ct)
    {
        var teamId = await db.Projects.Where(x => x.Id == projectId).Select(x => (long?)x.TeamId).SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Project", projectId);
        await LockTeam(teamId, ct);
        var project = await db.Projects.FromSqlInterpolated($"SELECT * FROM dbo.projects WITH (UPDLOCK,HOLDLOCK) WHERE id={projectId}").SingleAsync(ct);
        await RequireAccess(projectId, ct);
        if (project.Status != "ACTIVE") throw new ConflictException("Project must be ACTIVE.");
    }

    private async Task<long> TaskProject(long taskId, CancellationToken ct) =>
        await db.Tasks.Where(x => x.Id == taskId).Select(x => (long?)x.Milestone.ProjectId).SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Task", taskId);
    private async Task RequireAccess(long projectId, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(x => x.Id == projectId, ct)) throw new NotFoundException("Project", projectId);
        if (!await access.CanAccessAsync(Actor, projectId, ct)) throw new ForbiddenException();
    }
    private static void CheckToken(Guid actual, string expected)
    {
        if (!Guid.TryParse(expected, out var value) || actual != value) throw new ConflictException("The aggregate changed. Reload and retry.", WorkflowErrorCodes.StaleConcurrencyToken);
    }
    private Task Audit(string action, string entity, long id, object data, CancellationToken ct) =>
        audit.RecordAsync(new(Actor, action, entity, id, new Dictionary<string, object?> { ["data"] = data }), ct);
    private async Task<ProjectEvidenceDto[]> MapEvidenceAsync(IReadOnlyList<(ProjectEvidence Evidence, string EffectiveStatus)> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];
        var userIds = rows.Select(x => x.Evidence.SubmittedBy).Distinct().ToArray();
        var majorIds = rows.Where(x => x.Evidence.MajorId.HasValue).Select(x => x.Evidence.MajorId!.Value).Distinct().ToArray();
        var userNames = userIds.Length == 0 ? new Dictionary<long, string>()
            : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var majorNames = majorIds.Length == 0 ? new Dictionary<long, string>()
            : await db.Majors.AsNoTracking().Where(m => majorIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Name, ct);

        var taskIds = rows.Where(x => x.Evidence.SourceType == "TASK").Select(x => x.Evidence.SourceId).Distinct().ToArray();
        var deliverableIds = rows.Where(x => x.Evidence.SourceType == "DELIVERABLE").Select(x => x.Evidence.SourceId).Distinct().ToArray();
        var meetingIds = rows.Where(x => x.Evidence.SourceType == "MEETING").Select(x => x.Evidence.SourceId).Distinct().ToArray();
        var reportIds = rows.Where(x => x.Evidence.SourceType == "PROGRESS_REPORT").Select(x => x.Evidence.SourceId).Distinct().ToArray();
        var fileIds = rows.Where(x => x.Evidence.SourceType == "FILE").Select(x => x.Evidence.SourceId).Distinct().ToArray();

        var taskTitles = taskIds.Length == 0 ? new Dictionary<long, string>()
            : await db.Tasks.AsNoTracking().Where(t => taskIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Title, ct);
        var deliverableTitles = deliverableIds.Length == 0 ? new Dictionary<long, string>()
            : await db.Deliverables.AsNoTracking().Where(d => deliverableIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Title, ct);
        var meetingTitles = meetingIds.Length == 0 ? new Dictionary<long, string>()
            : await db.Meetings.AsNoTracking().Where(m => meetingIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Title, ct);
        var reportTitles = reportIds.Length == 0 ? new Dictionary<long, string>()
            : (await db.ProgressReports.AsNoTracking().Where(r => reportIds.Contains(r.Id))
                .Select(r => new { r.Id, r.Summary, r.PeriodStart, r.PeriodEnd }).ToArrayAsync(ct))
                .ToDictionary(r => r.Id, r => !string.IsNullOrWhiteSpace(r.Summary)
                    ? (r.Summary.Length <= 100 ? r.Summary : r.Summary[..100] + "…")
                    : $"Báo cáo tiến độ ({r.PeriodStart:dd/MM/yyyy} - {r.PeriodEnd:dd/MM/yyyy})");
        var fileTitles = fileIds.Length == 0 ? new Dictionary<long, string>()
            : await db.Files.AsNoTracking().Where(f => fileIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.OriginalFileName, ct);

        return rows.Select(item =>
        {
            var x = item.Evidence;
            string? sourceTitle = x.SourceType switch
            {
                "TASK" => taskTitles.GetValueOrDefault(x.SourceId),
                "DELIVERABLE" => deliverableTitles.GetValueOrDefault(x.SourceId),
                "MEETING" => meetingTitles.GetValueOrDefault(x.SourceId),
                "PROGRESS_REPORT" => reportTitles.GetValueOrDefault(x.SourceId),
                "FILE" => fileTitles.GetValueOrDefault(x.SourceId),
                _ => null
            };
            return new ProjectEvidenceDto(x.Id, x.ProjectId, x.SourceType, x.SourceId, x.MajorId,
                x.MajorId is null ? "UNCLASSIFIED" : "CLASSIFIED", item.EffectiveStatus, x.SubmittedBy, x.SubmittedAt, x.Notes,
                x.SourceType == "FILE" ? $"/api/v1/files/{x.SourceId}/download" : null,
                userNames.GetValueOrDefault(x.SubmittedBy),
                x.MajorId.HasValue ? majorNames.GetValueOrDefault(x.MajorId.Value) : null,
                sourceTitle);
        }).ToArray();
    }
}
