using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Projects.Validators;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class ProjectRequirementsService(AipmsDbContext db, ICurrentUser current,
    IProjectAccessService access, IProjectRepository projects, IAuditTrail audit, TimeProvider clock) : IProjectRequirementsService
{
    private long Actor => current.UserId ?? throw new UnauthorizedException();

    public Task<ProjectRequirementsDto> GetAsync(long projectId, CancellationToken ct) =>
        projects.InTransactionAsync(async token =>
        {
            await RequireAccess(projectId, token);
            return await Read(projectId, token);
        }, ct);

    public Task<ProjectRequirementsDto> ReplaceAsync(long projectId, ReplaceProjectRequirementsRequest request, CancellationToken ct) =>
        projects.InTransactionAsync(async token =>
        {
            var teamId = await db.Projects.Where(x => x.Id == projectId).Select(x => (long?)x.TeamId).SingleOrDefaultAsync(token)
                ?? throw new NotFoundException("Project", projectId);
            // Match the team -> project lock order used by submit and roster mutations.
            var team = await db.Teams.FromSqlInterpolated($"SELECT * FROM dbo.teams WITH (UPDLOCK,HOLDLOCK) WHERE id={teamId}").SingleAsync(token);
            var project = await db.Projects.FromSqlInterpolated($"SELECT * FROM dbo.projects WITH (UPDLOCK,HOLDLOCK) WHERE id={projectId}").SingleAsync(token);
            await RequireAccess(projectId, token);
            var actor = await db.Users.Where(x => x.Id == Actor && x.Status == "ACTIVE")
                .Select(x => new { x.DepartmentId, Roles = x.UserRoleUsers.Select(r => r.Role.Code).ToArray() }).SingleAsync(token);
            var academicScope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, token);
            var staff = actor.Roles.Contains("DEPARTMENT_STAFF") && actor.DepartmentId is long department
                && academicScope.DepartmentIds.Contains(department);
            var leader = actor.Roles.Contains("STUDENT") && await db.TeamMembers.AnyAsync(x => x.TeamId == teamId
                && x.UserId == Actor && x.LeftAt == null && x.IsLeader, token);
            if (!actor.Roles.Contains("ADMIN") && !staff && !leader) throw new ForbiddenException();
            if (project.Status is not ("DRAFT" or "REVISION_REQUIRED") || team.Status is "LOCKED" or "DISBANDED")
                throw new ConflictException("Project major requirements are locked. A revision must be opened before editing.");
            if (string.IsNullOrEmpty(request.ConcurrencyToken) || request.ConcurrencyToken != Convert.ToBase64String(project.RowVersion))
                throw new ConflictException("The project changed. Reload and retry.", WorkflowErrorCodes.StaleConcurrencyToken);
            ProjectRequirementsValidation.Validate(request.Requirements);
            var input = request.Requirements;
            var allowed = await db.ProjectMajors.Where(x => x.ProjectId == projectId && x.Major.IsActive
                && x.Major.Department.IsActive && x.Major.Department.Organization.IsActive).Select(x => x.MajorId).ToListAsync(token);
            if (input.Any(x => !allowed.Contains(x.MajorId))) throw new ConflictException("Every requirement must belong to the project's active academic scope.");
            var roster = await db.TeamMembers.Where(x => x.TeamId == teamId && x.LeftAt == null).Select(x => x.User.MajorId).ToListAsync(token);
            if (roster.Any(id => id is null || input.All(x => x.MajorId != id)))
                throw new ConflictException("Cannot remove a major represented by an active member.");
            var counts = roster.Where(x => x.HasValue).GroupBy(x => x!.Value).ToDictionary(x => x.Key, x => x.Count());
            var now = clock.GetUtcNow().UtcDateTime;
            var windows = await db.ProjectPeriods.Where(x => x.AcademicSemesterId == team.AcademicSemesterId
                && x.PeriodType == "REGISTRATION" && x.Status == "ACTIVE" && x.StartAt <= now && now <= x.EndAt
                && x.AcademicSemester.Status == "ACTIVE").ToListAsync(token);
            if (windows.Count != 1 || windows[0].MinTeamSize is null || windows[0].MaxTeamSize is null)
                throw new ConflictException("An unambiguous configured registration policy is required.");
            var policy = windows[0];
            var teamRequirements = await db.Set<TeamMajorRequirement>().Where(x => x.TeamId == teamId).ToListAsync(token);
            if (teamRequirements.Any(t => input.All(x => x.MajorId != t.MajorId)))
                throw new ConflictException("Project requirements must cover the configured team majors.");
            var bounds = input.Select(x =>
            {
                var teamRequirement = teamRequirements.SingleOrDefault(t => t.MajorId == x.MajorId);
                return new { Min = Math.Max(Math.Max(x.MinMembers, teamRequirement?.MinMembers ?? 0), counts.GetValueOrDefault(x.MajorId)),
                    Max = Math.Min(x.MaxMembers, teamRequirement?.MaxMembers ?? policy.MaxTeamSize!.Value) };
            }).ToArray();
            if (bounds.Any(x => x.Min > x.Max) || bounds.Sum(x => (long)x.Min) > policy.MaxTeamSize
                || bounds.Sum(x => (long)x.Max) < policy.MinTeamSize)
                throw new ConflictException("Project quotas are infeasible for the active roster, team quotas or period policy.");
            var old = await db.ProjectMajorRequirements.Where(x => x.ProjectId == projectId).ToListAsync(token);
            db.ProjectMajorRequirements.RemoveRange(old.Where(x => input.All(i => i.MajorId != x.MajorId)));
            foreach (var item in input)
            {
                var row = old.SingleOrDefault(x => x.MajorId == item.MajorId);
                if (row is null)
                {
                    row = new() { ProjectId = projectId, MajorId = item.MajorId, CreatedAt = now };
                    db.ProjectMajorRequirements.Add(row);
                }
                row.MinMembers = item.MinMembers; row.MaxMembers = item.MaxMembers;
                row.Responsibility = item.Responsibility.Trim(); row.UpdatedAt = now; row.ConcurrencyToken = Guid.NewGuid();
            }
            project.UpdatedAt = now;
            db.Entry(project).Property(x => x.UpdatedAt).IsModified = true;
            await db.SaveChangesAsync(token);
            await audit.RecordAsync(new(Actor, "PROJECT_MAJOR_REQUIREMENTS_REPLACED", "PROJECT", projectId,
                new Dictionary<string, object?> { ["majorIds"] = input.Select(x => x.MajorId).ToArray(), ["teamId"] = teamId }), token);
            await audit.RecordAsync(new(Actor, "TEAM_ELIGIBILITY_INVALIDATED", "TEAM", teamId,
                new Dictionary<string, object?> { ["projectId"] = projectId, ["reason"] = "PROJECT_REQUIREMENTS_CHANGED" }), token);
            return await Read(projectId, token);
        }, ct);

    public async Task<ProjectReviewHistoryDto> GetHistoryAsync(long projectId, int page, int pageSize, CancellationToken ct)
    {
        ProjectRequirementsValidation.ValidatePage(page, pageSize);
        await RequireAccess(projectId, ct);
        var actor = await db.Users.Where(x => x.Id == Actor).Select(x => new
        { x.DepartmentId, Roles = x.UserRoleUsers.Select(r => r.Role.Code).ToArray() }).SingleAsync(ct);
        var allRounds = db.Set<ProjectRegistrationSnapshot>().AsNoTracking().Where(x => x.ProjectId == projectId);
        var visible = allRounds;
        // Current project scope must not expose a past round from an unrelated department.
        if (!actor.Roles.Contains("ADMIN") && actor.Roles.Contains("DEPARTMENT_STAFF"))
            visible = visible.Where(x => x.LeadDepartmentId == actor.DepartmentId || x.Decisions.Any(d => d.DepartmentId == actor.DepartmentId));
        var count = await visible.CountAsync(ct);
        var rows = await visible.OrderByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { Snapshot = x, Number = allRounds.Count(s => s.Id <= x.Id) }).ToListAsync(ct);
        var ids = rows.Select(x => x.Snapshot.Id).ToArray();
        var decisions = await db.Set<ProjectDepartmentDecision>().AsNoTracking().Where(x => ids.Contains(x.SnapshotId))
            .OrderBy(x => x.DepartmentId).ToListAsync(ct);
        return new(page, pageSize, count, rows.Select(x =>
        {
            var s = x.Snapshot;
            var evidence = ProjectAcademicScopeReader.ReadHistoricalEvidence(s.SnapshotJson);
            return new ProjectReviewSnapshotDto(s.Id, x.Number, s.ProjectPeriodId, s.SubmittedBy, s.SubmittedAt, evidence,
                evidence?.Proposal is not null, decisions.Where(d => d.SnapshotId == s.Id)
                    .Select(d => new DepartmentDecisionDto(d.DepartmentId, d.Decision, d.DecidedBy, d.DecidedAt, d.Reason)).ToArray(),
                ProjectAcademicScopeReader.ParseEvidence(s.SnapshotJson) is null ? "UNKNOWN" : "FROZEN_REGISTRATION_SNAPSHOT");
        }).ToArray());
    }

    private async Task RequireAccess(long projectId, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(x => x.Id == projectId, ct)) throw new NotFoundException("Project", projectId);
        if (!await access.CanAccessAsync(Actor, projectId, ct)) throw new ForbiddenException();
    }

    private async Task<ProjectRequirementsDto> Read(long projectId, CancellationToken ct)
    {
        var version = await db.Projects.Where(x => x.Id == projectId).Select(x => x.RowVersion).SingleAsync(ct);
        var rows = await db.ProjectMajorRequirements.AsNoTracking().Where(x => x.ProjectId == projectId).OrderBy(x => x.MajorId).ToListAsync(ct);
        return new(Convert.ToBase64String(version), rows.Select(x => new ProjectMajorRequirementDto(x.Id, x.MajorId,
            x.MinMembers, x.MaxMembers, x.Responsibility, x.ConcurrencyToken.ToString("N"))).ToArray());
    }
}
