using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Teams.Abstractions;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Application.Features.Teams.Models;
using AIPMS.Application.Features.Teams.Services;
using AIPMS.Domain.Teams;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Repositories;

public sealed class TeamEligibilityRepository(
    AipmsDbContext context,
    ITeamFormationPolicyProvider policyProvider,
    ITeamRepository teamRepository)
    : ITeamEligibilityRepository
{
    public async Task<(TeamEligibilitySnapshotData Snapshot, bool WasInserted)> SaveCheckAsync(
        TeamEligibilitySaveModel model,
        CancellationToken cancellationToken)
    {
        // 1. Check if snapshot with same evaluation_key already exists for this team
        var existing = await context.TeamEligibilityChecks
            .Include(c => c.TeamEligibilityIssues)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TeamId == model.TeamId && c.EvaluationKey == model.Hashes.EvaluationKey, cancellationToken);

        if (existing is not null)
        {
            return (MapToSnapshotData(existing), false);
        }

        // 2. Prepare new entity and sorted issues
        var sortedIssues = (model.Issues ?? Array.Empty<TeamEligibilityIssueData>())
            .OrderBy(i => i.RuleCode, StringComparer.Ordinal)
            .ThenBy(i => i.MajorId ?? 0)
            .ThenBy(i => i.UserId ?? 0)
            .ThenBy(i => i.Message, StringComparer.Ordinal)
            .ToList();

        var checkEntity = new TeamEligibilityCheck
        {
            TeamId = model.TeamId,
            ProjectPeriodId = model.ProjectPeriodId,
            ProjectId = model.ProjectId,
            RoundType = model.RoundType,
            RevisionHistoryId = model.RevisionHistoryId,
            ProjectMode = model.ProjectMode,
            PolicyVersion = model.PolicyVersion,
            RuleVersion = model.RuleVersion,
            RosterHash = model.Hashes.RosterHash,
            AcademicScopeHash = model.Hashes.AcademicScopeHash,
            ProjectContextHash = model.Hashes.ProjectContextHash,
            Fingerprint = model.Hashes.Fingerprint,
            TemporalStateHash = model.Hashes.TemporalStateHash,
            EvaluationKey = model.Hashes.EvaluationKey,
            Result = model.Result,
            ValidUntilAt = model.Hashes.ValidUntilAt,
            CheckedBy = model.CheckedBy,
            CheckedAt = model.CheckedAt,
            TriggerSource = model.TriggerSource
        };

        var sortOrder = 1;
        foreach (var issue in sortedIssues)
        {
            checkEntity.TeamEligibilityIssues.Add(new TeamEligibilityIssue
            {
                SortOrder = sortOrder++,
                RuleCode = issue.RuleCode,
                Severity = issue.Severity,
                MajorId = issue.MajorId,
                UserId = issue.UserId,
                ExpectedValue = issue.ExpectedValue,
                ActualValue = issue.ActualValue,
                Message = issue.Message,
                CreatedAt = model.CheckedAt
            });
        }

        context.TeamEligibilityChecks.Add(checkEntity);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return (MapToSnapshotData(checkEntity), true);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Concurrent insert with same (team_id, evaluation_key) raced and won
            var racedExisting = await context.TeamEligibilityChecks
                .Include(c => c.TeamEligibilityIssues)
                .AsNoTracking()
                .FirstAsync(c => c.TeamId == model.TeamId && c.EvaluationKey == model.Hashes.EvaluationKey, cancellationToken);

            return (MapToSnapshotData(racedExisting), false);
        }
    }

    public async Task<TeamEligibilitySnapshotData?> GetLatestCheckAsync(
        long teamId,
        string? roundType,
        long? revisionHistoryId,
        CancellationToken cancellationToken)
    {
        var query = context.TeamEligibilityChecks
            .Include(c => c.TeamEligibilityIssues)
            .AsNoTracking()
            .Where(c => c.TeamId == teamId);

        if (!string.IsNullOrEmpty(roundType))
        {
            query = query.Where(c => c.RoundType == roundType);
        }

        if (revisionHistoryId.HasValue)
        {
            query = query.Where(c => c.RevisionHistoryId == revisionHistoryId.Value);
        }

        var entity = await query
            .OrderByDescending(c => c.CheckedAt)
            .ThenByDescending(c => c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return entity is null ? null : MapToSnapshotData(entity);
    }

    public async Task<TeamEligibilitySnapshotData?> GetCurrentSnapshotAsync(
        long teamId,
        long projectPeriodId,
        long? projectId,
        string roundType,
        long? revisionHistoryId,
        string evaluationKey,
        CancellationToken cancellationToken)
    {
        var entity = await context.TeamEligibilityChecks
            .Include(c => c.TeamEligibilityIssues)
            .AsNoTracking()
            .Where(c => c.TeamId == teamId
                        && c.EvaluationKey == evaluationKey
                        && c.ProjectPeriodId == projectPeriodId
                        && c.ProjectId == projectId
                        && c.RoundType == roundType
                        && c.RevisionHistoryId == revisionHistoryId)
            .FirstOrDefaultAsync(cancellationToken);

        return entity is null ? null : MapToSnapshotData(entity);
    }

    public async Task<IReadOnlyList<TeamEligibilitySnapshotData>> GetHistoryAsync(
        long teamId,
        CancellationToken cancellationToken)
    {
        var entities = await context.TeamEligibilityChecks
            .Include(c => c.TeamEligibilityIssues)
            .AsNoTracking()
            .Where(c => c.TeamId == teamId)
            .OrderByDescending(c => c.CheckedAt)
            .ThenByDescending(c => c.Id)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToSnapshotData).ToList();
    }

    public async Task<TeamEligibilityContextInput> BuildContextInputAsync(
        long teamId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var team = await context.Teams.AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == teamId, cancellationToken)
            ?? throw new NotFoundException("Team", teamId);

        // 1. Resolve ProjectPeriod
        var window = await teamRepository.GetOpenWindowAsync(team.AcademicSemesterId, utcNow, cancellationToken)
            ?? throw new ConflictException("No unambiguous active registration window exists in this semester.");

        var period = await context.ProjectPeriods.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == window.PeriodId, cancellationToken)
            ?? throw new ConflictException("No unambiguous active registration window exists in this semester.");

        // 2. Resolve Policy
        var policy = await policyProvider.GetAsync(period.Id, cancellationToken);
        if (policy is null || !policy.IsValid)
        {
            throw new ConflictException("Team formation policy is not configured for this registration period (BE-12).");
        }

        // 3. Resolve Academic Scope
        var scopeEntity = await context.Set<TeamAcademicConfiguration>()
            .Include(c => c.Requirements)
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.TeamId == teamId, cancellationToken);

        if (scopeEntity is null && policy.MinDistinctMajors > 1)
        {
            throw new ConflictException(TeamWorkflow.UnsupportedHybridPolicyMessage);
        }

        var result = await BuildHistoricalContextInputAsync(teamId, period.Id, utcNow, cancellationToken);
        return result ?? throw new ConflictException("Team formation policy is not configured for this registration period (BE-12).");
    }

    public async Task<TeamEligibilityContextInput?> BuildHistoricalContextInputAsync(
        long teamId,
        long projectPeriodId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var team = await context.Teams.AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == teamId, cancellationToken);
        if (team is null) return null;

        var period = await context.ProjectPeriods.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == projectPeriodId, cancellationToken);
        if (period is null) return null;

        var policy = await policyProvider.GetAsync(period.Id, cancellationToken);
        if (policy is null || !policy.IsValid) return null;

        var scopeEntity = await context.Set<TeamAcademicConfiguration>()
            .Include(c => c.Requirements)
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.TeamId == teamId, cancellationToken);

        if (scopeEntity is null && policy.MinDistinctMajors > 1) return null;

        AcademicScopeInput? scopeInput = null;
        if (scopeEntity is not null)
        {
            scopeInput = new AcademicScopeInput(
                PeriodId: period.Id,
                ProjectMode: scopeEntity.ProjectMode,
                PrimaryMajorId: scopeEntity.PrimaryMajorId,
                LeadDepartmentId: scopeEntity.LeadDepartmentId,
                Requirements: scopeEntity.Requirements
                    .OrderBy(r => r.MajorId)
                    .Select(r => new MajorRequirementInput(r.MajorId, r.MinMembers, r.MaxMembers, r.Responsibility))
                    .ToList());
        }

        var projectMode = scopeInput?.ProjectMode ?? "SINGLE_MAJOR";

        // 4. Resolve Roster Members & Qualifications
        var memberInputs = await BuildMemberInputsAsync(teamId, team.AcademicSemesterId, utcNow, cancellationToken);

        // 5. Resolve Project Context & Round
        var project = await context.Projects.AsNoTracking()
            .Include(p => p.ProjectMajors)
            .Include(p => p.ProjectTags)
                .ThenInclude(pt => pt.Tag)
            .Include(p => p.ProjectStatusHistories)
            .FirstOrDefaultAsync(p => p.TeamId == teamId, cancellationToken);

        string roundType;
        long? revisionHistoryId = null;
        ProjectContextInput? projectContextInput = null;

        if (project is not null)
        {
            var latestRevisionHistory = project.ProjectStatusHistories
                .Where(h => h.NewStatus == "REVISION_REQUIRED")
                .OrderByDescending(h => h.ChangedAt)
                .ThenByDescending(h => h.Id)
                .FirstOrDefault();

            if (project.Status == "REVISION_REQUIRED")
            {
                roundType = "REVISION";
                revisionHistoryId = latestRevisionHistory?.Id;
            }
            else
            {
                roundType = "INITIAL";
                revisionHistoryId = null;
            }

            var majorIds = project.ProjectMajors.Select(pm => pm.MajorId).OrderBy(m => m).ToList();
            var tags = project.ProjectTags
                .OrderBy(pt => pt.Tag.TagType, StringComparer.Ordinal)
                .ThenBy(pt => pt.Tag.Name, StringComparer.Ordinal)
                .Select(pt => new ProjectTagInput(pt.Tag.TagType, pt.Tag.Name))
                .ToList();

            projectContextInput = new ProjectContextInput(
                ProjectId: project.Id,
                ProposalSource: project.ProposalSource,
                TopicId: project.TopicId,
                Title: project.Title,
                ProblemStatement: project.ProblemStatement,
                Objectives: project.Objectives,
                ExpectedOutput: project.ExpectedOutput,
                MajorIds: majorIds,
                Tags: tags,
                Requirements: await context.ProjectMajorRequirements.AsNoTracking().Where(x => x.ProjectId == project.Id)
                    .OrderBy(x => x.MajorId).Select(x => new AIPMS.Application.Features.Projects.DTOs.ProjectMajorRequirementDto(
                        x.Id, x.MajorId, x.MinMembers, x.MaxMembers, x.Responsibility, x.ConcurrencyToken.ToString("N"))).ToArrayAsync(cancellationToken));
        }
        else
        {
            roundType = "FORMATION";
            revisionHistoryId = null;
            projectContextInput = null;
        }

        return new TeamEligibilityContextInput(
            TeamId: team.Id,
            ProjectPeriodId: period.Id,
            ProjectId: project?.Id,
            RoundType: roundType,
            RevisionHistoryId: revisionHistoryId,
            ProjectMode: projectMode,
            PolicyVersion: policy.Version,
            RuleVersion: TeamEligibilityRuleSet.Version,
            Members: memberInputs,
            Scope: scopeInput,
            Project: projectContextInput,
            Policy: policy);
    }

    private async Task<List<RosterMemberInput>> BuildMemberInputsAsync(
        long teamId,
        long semesterId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var memberships = await context.TeamMembers.AsNoTracking()
            .Where(m => m.TeamId == teamId && m.LeftAt == null)
            .Select(m => new { m.UserId, m.IsLeader })
            .ToListAsync(cancellationToken);

        var memberUserIds = memberships.Select(m => m.UserId).ToArray();

        var users = await context.Users.AsNoTracking()
            .Include(u => u.Major)
                .ThenInclude(m => m!.Department)
                    .ThenInclude(d => d.Organization)
            .Include(u => u.UserRoleUsers)
                .ThenInclude(r => r.Role)
            .Where(u => memberUserIds.Contains(u.Id))
            .ToListAsync(cancellationToken);

        var memberInputs = new List<RosterMemberInput>();
        foreach (var membership in memberships)
        {
            var user = users.SingleOrDefault(u => u.Id == membership.UserId);
            if (user is null) continue;

            var isEligibleStudent = user.Status == "ACTIVE"
                && user.AcademicProfileStatus == "VERIFIED"
                && user.UserRoleUsers.Any(r => r.Role.Code == "STUDENT")
                && user.Major != null
                && user.Major.IsActive
                && user.Major.Department != null
                && user.Major.Department.IsActive
                && user.Major.Department.Organization != null
                && user.Major.Department.Organization.IsActive
                && user.DepartmentId == user.Major.DepartmentId;

            var qualification = await teamRepository.GetQualificationEligibilityAsync(
                user.Id, semesterId, utcNow, cancellationToken);

            memberInputs.Add(new RosterMemberInput(
                UserId: user.Id,
                FullName: user.FullName,
                MajorId: user.MajorId,
                OrganizationId: user.Major?.Department?.OrganizationId,
                IsEligibleStudent: isEligibleStudent,
                IsLeader: membership.IsLeader,
                QualificationRequired: qualification.Required,
                QualificationEligible: qualification.Eligible,
                QualificationIssueCode: qualification.IssueCode,
                QualificationValidUntilAt: qualification.ExpiresAt));
        }

        return memberInputs;
    }

    private static TeamEligibilitySnapshotData MapToSnapshotData(TeamEligibilityCheck entity)
    {
        var sortedIssues = entity.TeamEligibilityIssues
            .OrderBy(i => i.SortOrder)
            .Select(i => new TeamEligibilityIssueDto(
                Id: i.Id,
                EligibilityCheckId: i.EligibilityCheckId,
                SortOrder: i.SortOrder,
                RuleCode: i.RuleCode,
                Severity: i.Severity,
                MajorId: i.MajorId,
                UserId: i.UserId,
                ExpectedValue: i.ExpectedValue,
                ActualValue: i.ActualValue,
                Message: i.Message,
                CreatedAt: i.CreatedAt))
            .ToList();

        return new TeamEligibilitySnapshotData(
            Id: entity.Id,
            TeamId: entity.TeamId,
            ProjectPeriodId: entity.ProjectPeriodId,
            ProjectId: entity.ProjectId,
            RoundType: entity.RoundType,
            RevisionHistoryId: entity.RevisionHistoryId,
            ProjectMode: entity.ProjectMode,
            PolicyVersion: entity.PolicyVersion,
            RuleVersion: entity.RuleVersion,
            RosterHash: entity.RosterHash,
            AcademicScopeHash: entity.AcademicScopeHash,
            ProjectContextHash: entity.ProjectContextHash,
            Fingerprint: entity.Fingerprint,
            TemporalStateHash: entity.TemporalStateHash,
            EvaluationKey: entity.EvaluationKey,
            Result: entity.Result,
            ValidUntilAt: entity.ValidUntilAt,
            CheckedBy: entity.CheckedBy,
            CheckedAt: entity.CheckedAt,
            TriggerSource: entity.TriggerSource,
            Issues: sortedIssues);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        if (ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2601 || sqlEx.Number == 2627))
        {
            return true;
        }

        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("uq_team_eligibility_checks_team_eval", StringComparison.OrdinalIgnoreCase)
            || message.Contains("2601", StringComparison.Ordinal)
            || message.Contains("2627", StringComparison.Ordinal);
    }
}
