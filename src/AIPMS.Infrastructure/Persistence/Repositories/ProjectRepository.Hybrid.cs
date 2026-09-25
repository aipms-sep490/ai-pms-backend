using System.Data;
using System.Text.Json;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Domain.Teams;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace AIPMS.Infrastructure.Persistence.Repositories;

public sealed partial class ProjectRepository
{
    private DateTime Now => (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    public async Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        if (context.Database.CurrentTransaction is not null) return await action(ct);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var result = await action(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            for (var cause = exception; cause is not null; cause = cause.InnerException)
                if (cause is DbUpdateConcurrencyException or SqlException { Number: 1205 or 2601 or 2627 })
                    throw new ConflictException("Project changed concurrently. Refresh and retry.");
            throw;
        }
    }

    private async Task<TeamAcademicScope?> GetTeamScopeAsync(long teamId, CancellationToken ct) =>
        (await context.Set<TeamAcademicConfiguration>().AsNoTracking().Include(c => c.Requirements)
            .SingleOrDefaultAsync(c => c.TeamId == teamId, ct))?.ToScope();

    private async Task<Project> LockProjectAsync(long projectId, CancellationToken ct)
    {
        var teamId = await context.Projects.Where(p => p.Id == projectId).Select(p => (long?)p.TeamId)
            .SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Project", projectId);
        await context.Teams.FromSqlInterpolated($"SELECT * FROM dbo.teams WITH (UPDLOCK, HOLDLOCK) WHERE id = {teamId}").SingleAsync(ct);
        return await context.Projects.FromSqlInterpolated($"SELECT * FROM dbo.projects WITH (UPDLOCK, HOLDLOCK) WHERE id = {projectId}").SingleAsync(ct);
    }

    public async System.Threading.Tasks.Task LockProjectAndTopicAsync(long projectId, long topicId, CancellationToken ct)
    {
        var teamId = await context.Projects.Where(p => p.Id == projectId).Select(p => (long?)p.TeamId)
            .SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Project", projectId);
        await context.Teams.FromSqlInterpolated($"SELECT * FROM dbo.teams WITH (UPDLOCK, HOLDLOCK) WHERE id = {teamId}").SingleAsync(ct);
        await context.Projects.FromSqlInterpolated($"SELECT * FROM dbo.projects WITH (UPDLOCK, HOLDLOCK) WHERE id = {projectId}").SingleAsync(ct);
        await context.TeamMembers.FromSqlInterpolated($"SELECT * FROM dbo.team_members WITH (UPDLOCK, HOLDLOCK) WHERE team_id = {teamId}").AsNoTracking().ToListAsync(ct);
        await context.Set<ProjectTopic>().FromSqlInterpolated($"SELECT * FROM dbo.project_topics WITH (UPDLOCK, HOLDLOCK) WHERE id = {topicId}").AsNoTracking().SingleOrDefaultAsync(ct);
    }

    private async System.Threading.Tasks.Task ValidateProjectMajorsAsync(long teamId, IReadOnlyList<long> majorIds, CancellationToken ct)
    {
        var scope = await GetTeamScopeAsync(teamId, ct);
        if (scope is not null && !majorIds.Order().SequenceEqual(scope.Requirements.Select(r => r.MajorId).Order()))
            throw new ConflictException("Project required majors must match the team's academic scope. Update the scope and proposal together before submitting.");
    }

    private async Task<ProjectPeriod?> ValidateGovernanceAsync(long teamId, IReadOnlyList<long> majorIds,
        string source, CancellationToken ct)
    {
        var semesterId = await context.Teams.Where(t => t.Id == teamId).Select(t => t.AcademicSemesterId).SingleAsync(ct);
        var window = await new TeamRepository(context).GetOpenWindowAsync(semesterId, Now, ct);
        // Registration guards retain their existing missing-window behavior for legacy repository callers.
        if (window is null) return null;
        var mode = (await GetTeamScopeAsync(teamId, ct))?.ProjectMode
            ?? (majorIds.Distinct().Count() > 1 ? "INTERDISCIPLINARY" : "SINGLE_MAJOR");
        if (!ProjectPeriodGovernancePolicy.Allows(window.AllowedProjectModes, mode))
            throw new ConflictException("PROJECT_MODE_NOT_ALLOWED_BY_PERIOD");
        if (!ProjectPeriodGovernancePolicy.Allows(window.AllowedProposalSources, source))
            throw new ConflictException("PROPOSAL_SOURCE_NOT_ALLOWED_BY_PERIOD");
        return await context.ProjectPeriods.SingleAsync(p => p.Id == window.PeriodId, ct);
    }

    private async System.Threading.Tasks.Task CaptureRegistrationAsync(Project project, long actorId, DateTime now, CancellationToken ct)
    {
        if (await context.TeamMembers.AnyAsync(m => m.TeamId == project.TeamId && m.LeftAt == null
            && (m.User.AcademicProfileStatus != "VERIFIED" || m.User.Status != "ACTIVE"), ct))
            throw new ConflictException("Every active team member must have a verified academic profile before submission.");
        var projectMajorIds = await context.ProjectMajors.Where(m => m.ProjectId == project.Id).Select(m => m.MajorId).ToArrayAsync(ct);
        await ValidateGovernanceAsync(project.TeamId, projectMajorIds, project.ProposalSource, ct);
        var scope = await GetTeamScopeAsync(project.TeamId, ct);
        if (scope is null)
        {
            await CaptureLegacyRegistrationAsync(project, actorId, now, projectMajorIds, ct);
            return;
        }
        var teams = new TeamRepository(context);
        var team = await teams.GetAsync(project.TeamId, ct) ?? throw new NotFoundException("Team", project.TeamId);
        var window = await teams.GetOpenWindowAsync(team.SemesterId, now, ct)
            ?? throw new ConflictException("Registration window is unavailable.");
        var period = await context.ProjectPeriods.SingleAsync(p => p.Id == window.PeriodId, ct);
        if (period.MinTeamSize is null && period.MaxTeamSize is null) throw new ConflictException("Team policy is not configured.");
        var min = period.MinTeamSize ?? 3; var max = period.MaxTeamSize ?? 5; var distinct = period.MinDistinctMajors ?? 1;
        var policy = new TeamFormationPolicy(min, max, 24, $"v-{period.Id}-{min}-{max}-{distinct}-g{period.PolicyVersion}", distinct);
        var errors = HybridTeamRules.EligibilityErrors(team.Members, policy, window.OrganizationId, scope);
        if (errors.Count != 0) throw new ConflictException(string.Join(", ", errors));
        await teams.ValidateAcademicScopeAsync(scope, window.OrganizationId, ct);
        var majors = await context.ProjectMajors.Where(m => m.ProjectId == project.Id).Select(m => m.MajorId).ToArrayAsync(ct);
        await ValidateProjectMajorsAsync(project.TeamId, majors, ct);
        var requiredIds = scope.Requirements.Select(r => r.MajorId).ToArray();
        var departments = await context.Majors.Where(m => requiredIds.Contains(m.Id)).Select(m => m.DepartmentId).Distinct().OrderBy(id => id).ToArrayAsync(ct);
        var evidence = new RegistrationEvidence(TeamAcademicScopeDto.FromScope(scope), new(min, max, distinct, policy.Version),
            window.OrganizationId, period.StartAt, period.EndAt,
            team.Members.Select(m => new RegisteredMemberDto(m.UserId, m.FullName, m.MajorId!.Value, m.IsLeader)).ToArray(), departments,
            period.PolicyVersion, period.AllowedProjectModes, period.AllowedProposalSources, project.ProposalSource,
            await context.Majors.Where(m => requiredIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.DepartmentId, ct));
        context.Add(new ProjectRegistrationSnapshot
        {
            ProjectId = project.Id, ProjectPeriodId = period.Id, SubmittedBy = actorId, SubmittedAt = now,
            LeadDepartmentId = scope.LeadDepartmentId,
            SnapshotJson = JsonSerializer.Serialize(evidence),
            Decisions = scope.ProjectMode == "INTERDISCIPLINARY"
                ? departments.Select(id => new ProjectDepartmentDecision { DepartmentId = id }).ToList() : []
        });
    }

    private async System.Threading.Tasks.Task CaptureLegacyRegistrationAsync(Project project, long actorId, DateTime now,
        IReadOnlyList<long> majorIds, CancellationToken ct)
    {
        var semesterId = await context.Teams.Where(t => t.Id == project.TeamId).Select(t => t.AcademicSemesterId).SingleAsync(ct);
        var window = await new TeamRepository(context).GetOpenWindowAsync(semesterId, now, ct);
        if (window is null) return; // Older repository callers retain the registration guard's window contract.
        var period = await context.ProjectPeriods.SingleAsync(p => p.Id == window.PeriodId, ct);
        var departments = await context.Majors.Where(m => majorIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.DepartmentId, ct);
        var departmentIds = departments.Values.Distinct().ToArray();
        if (departmentIds.Length != 1)
            throw new ConflictException("Configure a lead department and academic scope before submitting a multi-department proposal.");
        var members = await context.TeamMembers.Where(m => m.TeamId == project.TeamId && m.LeftAt == null && m.User.MajorId != null)
            .Select(m => new RegisteredMemberDto(m.UserId, m.User.FullName, m.User.MajorId!.Value, m.IsLeader)).ToListAsync(ct);
        var mode = majorIds.Count > 1 ? "INTERDISCIPLINARY" : "SINGLE_MAJOR";
        var max = period.MaxTeamSize ?? 5;
        var scope = new TeamAcademicScopeDto(mode, mode == "SINGLE_MAJOR" ? majorIds.Single() : null,
            departmentIds[0], majorIds.Select(id => new MajorRequirementDto(id, 0, max, "Legacy project major")).ToArray(), Guid.Empty);
        var evidence = new RegistrationEvidence(scope, new(period.MinTeamSize ?? 3, max, period.MinDistinctMajors ?? 1,
                $"v-{period.Id}-{period.MinTeamSize ?? 3}-{max}-{period.MinDistinctMajors ?? 1}-g{period.PolicyVersion}"),
            window.OrganizationId, period.StartAt, period.EndAt, members, departmentIds,
            period.PolicyVersion, period.AllowedProjectModes, period.AllowedProposalSources, project.ProposalSource, departments);
        context.Add(new ProjectRegistrationSnapshot { ProjectId = project.Id, ProjectPeriodId = period.Id,
            SubmittedBy = actorId, SubmittedAt = now, LeadDepartmentId = departmentIds[0], SnapshotJson = JsonSerializer.Serialize(evidence) });
    }

    private Task<ProjectRegistrationSnapshot?> LatestRegistrationAsync(long projectId, CancellationToken ct) =>
        context.Set<ProjectRegistrationSnapshot>().Include(s => s.Decisions)
            .Where(s => s.ProjectId == projectId).OrderByDescending(s => s.Id).FirstOrDefaultAsync(ct);

    private IQueryable<long> DepartmentProjectIds(long? departmentId)
    {
        var snapshots = context.Set<ProjectRegistrationSnapshot>();
        var latest = snapshots.Where(s => !snapshots.Any(newer => newer.ProjectId == s.ProjectId && newer.Id > s.Id));
        return context.Projects.Where(p =>
            ((p.Status == "DRAFT" || p.Status == "REVISION_REQUIRED" || !latest.Any(s => s.ProjectId == p.Id))
                && p.ProjectMajors.Any(m => m.Major.DepartmentId == departmentId))
            || (p.Status != "DRAFT" && p.Status != "REVISION_REQUIRED" && latest.Any(s => s.ProjectId == p.Id
                && (s.LeadDepartmentId == departmentId || s.Decisions.Any(d => d.DepartmentId == departmentId)))))
            .Select(p => p.Id);
    }

    public async Task<ProjectAcademicReviewDto> GetAcademicReviewAsync(long projectId, CancellationToken ct)
    {
        var project = await context.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new NotFoundException("Project", projectId);
        var snapshot = await LatestRegistrationAsync(projectId, ct);
        var dto = snapshot is null ? null : new RegistrationSnapshotDto(snapshot.Id, snapshot.ProjectPeriodId,
            snapshot.SubmittedBy, snapshot.SubmittedAt, JsonSerializer.Deserialize<RegistrationEvidence>(snapshot.SnapshotJson)!,
            snapshot.Decisions.OrderBy(d => d.DepartmentId).Select(d => new DepartmentDecisionDto(d.DepartmentId,
                d.Decision, d.DecidedBy, d.DecidedAt, d.Reason)).ToArray());
        var scope = await GetTeamScopeAsync(project.TeamId, ct);
        var effectiveScope = project.Status is not ("DRAFT" or "REVISION_REQUIRED") && dto is not null
            ? dto.Evidence.Scope : scope is null ? null : TeamAcademicScopeDto.FromScope(scope);
        var rounds = await context.Set<ProjectRegistrationSnapshot>().AsNoTracking().Include(s => s.Decisions)
            .Where(s => s.ProjectId == projectId).OrderByDescending(s => s.Id).ToListAsync(ct);
        var history = rounds.Select(s => new RegistrationSnapshotDto(s.Id, s.ProjectPeriodId, s.SubmittedBy,
            s.SubmittedAt, JsonSerializer.Deserialize<RegistrationEvidence>(s.SnapshotJson)!,
            s.Decisions.OrderBy(d => d.DepartmentId).Select(d => new DepartmentDecisionDto(d.DepartmentId,
                d.Decision, d.DecidedBy, d.DecidedAt, d.Reason)).ToArray())).ToArray();
        return new(Convert.ToBase64String(project.RowVersion), effectiveScope, dto, history);
    }

    private async Task<long> RequireAcademicReviewerAsync(long actorId, CancellationToken ct)
    {
        var departmentId = await context.Users.Where(u => u.Id == actorId && u.Status == "ACTIVE"
                && u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF") && u.Department != null
                && u.Department.IsActive && u.Department.Organization.IsActive)
            .Select(u => u.DepartmentId).SingleOrDefaultAsync(ct);
        return departmentId ?? throw new ForbiddenException("An active department staff assignment is required for academic decisions.");
    }

    private async System.Threading.Tasks.Task ValidateAcademicReviewTransitionAsync(Project project, string newStatus, long actorId, CancellationToken ct)
    {
        if (newStatus is not ("UNDER_REVIEW" or "REVISION_REQUIRED" or "REJECTED" or "APPROVED")) return;
        var departmentId = await RequireAcademicReviewerAsync(actorId, ct);
        if (await GetTeamScopeAsync(project.TeamId, ct) is null)
        {
            var legacyScope = await AIPMS.Infrastructure.Services.Projects.ProjectAcademicScopeReader.ReadAsync(context, project.Id, ct);
            if (legacyScope.LeadDepartmentId != departmentId)
                throw new ForbiddenException("Only the responsible department can change the proposal review state.");
            return;
        }
        var snapshot = await LatestRegistrationAsync(project.Id, ct)
            ?? throw new ConflictException("This proposal must be submitted with a current academic scope snapshot.");
        var evidence = JsonSerializer.Deserialize<RegistrationEvidence>(snapshot.SnapshotJson)!;
        if (departmentId != evidence.Scope.LeadDepartmentId)
            throw new ForbiddenException("Only the lead department can change the proposal review state; participating departments record their own decision.");
        if (newStatus == "APPROVED" && evidence.Scope.ProjectMode == "INTERDISCIPLINARY"
            && (snapshot.Decisions.Count != evidence.DepartmentIds.Count || snapshot.Decisions.Any(d => d.Decision != "APPROVED")))
            throw new ConflictException("Every participating department must approve this submission before final approval.");
    }

    public async Task<ProjectAcademicReviewDto> RecordDepartmentDecisionAsync(long projectId, long actorId,
        DepartmentDecisionRequest request, CancellationToken ct)
    {
        if (context.Database.CurrentTransaction is null) throw new InvalidOperationException("Department decisions require a transaction.");
        var project = await LockProjectAsync(projectId, ct);
        if (project.Status != "UNDER_REVIEW") throw new ConflictException("Department decisions require UNDER_REVIEW.");
        if (Convert.ToBase64String(project.RowVersion) != request.ConcurrencyToken) throw new ConflictException("Project changed. Refresh and retry.");
        var snapshot = await LatestRegistrationAsync(projectId, ct);
        if (snapshot is null || snapshot.Id != request.SnapshotId) throw new ConflictException("The submission has changed. Review the latest snapshot.");
        var departmentId = await RequireAcademicReviewerAsync(actorId, ct);
        var decision = snapshot.Decisions.SingleOrDefault(d => d.DepartmentId == departmentId)
            ?? throw new ForbiddenException("Your department is not a required reviewer for this submission.");
        if (decision.Decision != "PENDING") throw new ConflictException("This department has already decided. Request a revision for a new review round.");
        if (request.Decision is not ("APPROVED" or "REJECTED") || (request.Decision == "REJECTED" && string.IsNullOrWhiteSpace(request.Reason)))
            throw new ConflictException("A valid decision and rejection reason are required.");
        decision.Decision = request.Decision; decision.Reason = request.Reason?.Trim();
        decision.DecidedBy = actorId; decision.DecidedAt = Now;
        // Touch the proposal even with a fixed test clock, so its rowversion arbitrates decisions and final approval.
        project.UpdatedAt = Now;
        context.Entry(project).Property(p => p.UpdatedAt).IsModified = true;
        await context.SaveChangesAsync(ct);
        return await GetAcademicReviewAsync(projectId, ct);
    }
}
