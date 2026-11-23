using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Services;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class IndividualProcessScoringService(
    AipmsDbContext db,
    IEvaluationDraftRepository transactions,
    ICurrentUser current,
    IAuditTrail audit,
    TimeProvider clock) : IIndividualProcessScoringService
{
    private long Actor => current.IsAuthenticated && current.UserId.HasValue
        ? current.UserId.Value
        : throw new UnauthorizedException();

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private async Task<IReadOnlyList<GateProcessWeightInput>> LoadGateWeightsAsync(long projectId, CancellationToken ct)
    {
        var governanceJson = await db.Set<EvaluationScheme>().AsNoTracking()
            .Where(s => s.ProjectId == projectId && s.Status == "PUBLISHED")
            .Select(s => s.GovernanceJson)
            .FirstOrDefaultAsync(ct)
            ?? await db.Set<EvaluationScheme>().AsNoTracking()
                .Where(s => s.ProjectId == projectId)
                .OrderByDescending(s => s.Version)
                .Select(s => s.GovernanceJson)
                .FirstOrDefaultAsync(ct);

        if (!string.IsNullOrWhiteSpace(governanceJson))
        {
            var policy = JsonSerializer.Deserialize<SchemePolicyInput>(governanceJson);
            return VersionedResultCalculator.EffectiveGateWeights(policy);
        }
        return VersionedResultCalculator.DefaultIndividualGateWeights;
    }

    public Task<ProjectIndividualProcessScoresDto> GetProjectScoresAsync(long projectId, CancellationToken ct) =>
        transactions.InTransactionAsync(async () =>
        {
            var actor = await transactions.GetActorAsync(Actor, ct) ?? throw new ForbiddenException();
            var project = await db.Projects.AsNoTracking()
                .Where(p => p.Id == projectId)
                .Select(p => new { p.Id, p.TeamId, p.Status })
                .SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Project", projectId);

            var evalProject = await transactions.GetProjectAsync(projectId, ct);
            var isPrimary = await db.SupervisorAssignments.AsNoTracking().AnyAsync(a =>
                a.ProjectId == projectId && a.EndedAt == null && a.IsPrimary && a.AssignmentType == "PRIMARY"
                && a.SupervisorProfile.UserId == Actor, ct);
            var mentorMajors = await db.SupervisorAssignments.AsNoTracking()
                .Where(a => a.ProjectId == projectId && a.EndedAt == null && a.AssignmentType == "DISCIPLINE_MENTOR"
                    && a.MajorId != null && a.SupervisorProfile.UserId == Actor)
                .Select(a => a.MajorId!.Value)
                .Distinct()
                .ToArrayAsync(ct);
            var isStaffOrAdmin = actor.IsAdmin || (evalProject is not null && AcademicAuthority.HasScope(actor, evalProject));
            var now = Now;
            var isEvaluator = await db.Set<EvaluationAssignment>().AsNoTracking().AnyAsync(a =>
                a.ProjectId == projectId && a.EvaluatorId == Actor && a.Status == "ACTIVE"
                && (a.ValidFrom == null || a.ValidFrom <= now)
                && (a.ValidUntil == null || a.ValidUntil >= now), ct)
                || await db.Set<DefenseCommitteeMember>().AsNoTracking().AnyAsync(m =>
                    m.UserId == Actor && db.Set<DefenseCommittee>().Any(c =>
                        c.Id == m.CommitteeId && c.ProjectId == projectId && c.Status == "CONFIRMED"), ct);
            var isStudentMember = await db.TeamMembers.AsNoTracking().AnyAsync(m =>
                m.TeamId == project.TeamId && m.UserId == Actor && m.LeftAt == null, ct);

            string viewerRole;
            bool canEdit = false;
            bool finalizedOnly = false;
            long? filterStudentId = null;
            HashSet<long>? filterMajors = null;

            if (isPrimary)
            {
                viewerRole = "PRIMARY_SUPERVISOR";
                canEdit = project.Status is "ACTIVE" or "FINAL_SUBMISSION";
            }
            else if (isStaffOrAdmin)
            {
                viewerRole = actor.IsAdmin ? "ADMIN" : "DEPARTMENT_STAFF";
            }
            else if (mentorMajors.Length > 0)
            {
                viewerRole = "DISCIPLINE_MENTOR";
                filterMajors = mentorMajors.ToHashSet();
            }
            else if (isEvaluator)
            {
                viewerRole = "COUNCIL_EVALUATOR";
                finalizedOnly = true;
            }
            else if (isStudentMember)
            {
                var published = await db.Set<StudentResult>().AsNoTracking()
                    .AnyAsync(r => r.ProjectId == projectId && r.StudentId == Actor, ct);
                if (!published)
                    throw new ForbiddenException("Individual process scores are hidden until results are published.", "RESULT_NOT_PUBLISHED");
                viewerRole = "STUDENT";
                finalizedOnly = true;
                filterStudentId = Actor;
            }
            else
            {
                throw new ForbiddenException("You are not authorized to view individual process scores for this project.");
            }

            var weights = await LoadGateWeightsAsync(projectId, ct);
            var members = await db.TeamMembers.AsNoTracking()
                .Where(m => m.TeamId == project.TeamId && m.LeftAt == null)
                .Select(m => new { m.UserId, m.User.FullName, m.User.MajorId })
                .OrderBy(m => m.UserId)
                .ToListAsync(ct);

            if (filterStudentId.HasValue)
                members = members.Where(m => m.UserId == filterStudentId.Value).ToList();
            if (filterMajors is not null)
                members = members.Where(m => m.MajorId.HasValue && filterMajors.Contains(m.MajorId.Value)).ToList();

            var gateMap = await db.ProjectGateInstances.AsNoTracking()
                .Where(g => g.ProjectId == projectId)
                .Select(g => new { g.Id, g.Sequence })
                .ToDictionaryAsync(g => g.Id, g => g.Sequence, ct);

            var scores = await db.Set<GateIndividualProcessScore>().AsNoTracking()
                .Where(s => s.ProjectId == projectId)
                .ToListAsync(ct);

            var studentDtos = new List<StudentIndividualProcessSummaryDto>(members.Count);
            foreach (var member in members)
            {
                var memberScores = scores.Where(s => s.StudentId == member.UserId)
                    .Where(s => !finalizedOnly || s.Status == "FINALIZED")
                    .OrderBy(s => gateMap.GetValueOrDefault(s.GateInstanceId))
                    .Select(s => Map(s, gateMap.GetValueOrDefault(s.GateInstanceId), hideInternalNotes: finalizedOnly && viewerRole == "STUDENT"))
                    .ToArray();

                var finalizedByGate = memberScores
                    .Where(s => s.Status == "FINALIZED" && s.Score.HasValue)
                    .ToDictionary(s => s.GateNumber, s => s.Score!.Value);

                var complete = weights.All(w => finalizedByGate.ContainsKey(w.GateNumber));
                decimal? cumulative = complete
                    ? decimal.Round(weights.Sum(w => finalizedByGate[w.GateNumber] * w.WeightPercent / 100m), 2, MidpointRounding.AwayFromZero)
                    : null;

                studentDtos.Add(new(member.UserId, member.FullName, member.MajorId, cumulative, complete, memberScores));
            }

            return new ProjectIndividualProcessScoresDto(projectId, viewerRole, canEdit, weights, studentDtos);
        }, ct);

    public Task<GateIndividualProcessScoreItemDto> SaveDraftAsync(
        long projectId, long gateInstanceId, long studentId, UpsertIndividualProcessScoreRequest input, CancellationToken ct) =>
        transactions.InTransactionAsync(async () =>
        {
            await transactions.LockProjectAsync(projectId, ct);
            var (assignmentId, gateNumber, gateWeight) = await ValidatePrimaryWriteContextAsync(projectId, gateInstanceId, studentId, ct);
            if (input.Score is < 0 or > 10 || decimal.Round(input.Score, 2) != input.Score)
                throw new ValidationException(new Dictionary<string, string[]> { ["score"] = ["Score must be between 0.00 and 10.00 with at most 2 decimal places."] });
            if (input.Comments?.Length > 2000 || input.MentorReferenceNotes?.Length > 2000)
                throw new ValidationException(new Dictionary<string, string[]> { ["comments"] = ["Comments and mentorReferenceNotes must be <= 2000 characters."] });

            var existing = await db.Set<GateIndividualProcessScore>()
                .SingleOrDefaultAsync(s => s.GateInstanceId == gateInstanceId && s.StudentId == studentId, ct);
            var now = Now;
            if (existing is null)
            {
                existing = new GateIndividualProcessScore
                {
                    ProjectId = projectId,
                    GateInstanceId = gateInstanceId,
                    StudentId = studentId,
                    SupervisorAssignmentId = assignmentId,
                    Score = input.Score,
                    GateWeightPercent = gateWeight,
                    MentorReferenceNotes = string.IsNullOrWhiteSpace(input.MentorReferenceNotes) ? null : input.MentorReferenceNotes.Trim(),
                    Comments = string.IsNullOrWhiteSpace(input.Comments) ? null : input.Comments.Trim(),
                    Status = "DRAFT",
                    ConcurrencyToken = Guid.NewGuid(),
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.Add(existing);
            }
            else
            {
                if (existing.Status == "FINALIZED")
                    throw new ConflictException("Finalized individual process scores are immutable.", WorkflowErrorCodes.ImmutableVersion);
                if (!string.IsNullOrWhiteSpace(input.ConcurrencyToken)
                    && (!Guid.TryParse(input.ConcurrencyToken, out var parsed) || parsed != existing.ConcurrencyToken))
                    throw new ConflictException("Individual process score has changed. Reload before retrying.", WorkflowErrorCodes.StaleConcurrencyToken);
                existing.SupervisorAssignmentId = assignmentId;
                existing.Score = input.Score;
                existing.GateWeightPercent = gateWeight;
                if (input.Comments is not null)
                    existing.Comments = string.IsNullOrWhiteSpace(input.Comments) ? null : input.Comments.Trim();
                if (input.MentorReferenceNotes is not null)
                    existing.MentorReferenceNotes = string.IsNullOrWhiteSpace(input.MentorReferenceNotes) ? null : input.MentorReferenceNotes.Trim();
                existing.UpdatedAt = now;
                existing.ConcurrencyToken = Guid.NewGuid();
            }

            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(new(Actor, "GATE_INDIVIDUAL_PROCESS_SCORE_SAVED", "GATE_INDIVIDUAL_PROCESS_SCORE", existing.Id,
                new Dictionary<string, object?> { ["projectId"] = projectId, ["gateNumber"] = gateNumber, ["studentId"] = studentId, ["score"] = existing.Score }), ct);
            return Map(existing, gateNumber, false);
        }, ct);

    public Task<GateIndividualProcessScoreItemDto> FinalizeAsync(
        long projectId, long gateInstanceId, long studentId, FinalizeIndividualProcessScoreRequest input, CancellationToken ct) =>
        transactions.InTransactionAsync(async () =>
        {
            await transactions.LockProjectAsync(projectId, ct);
            var (assignmentId, gateNumber, _) = await ValidatePrimaryWriteContextAsync(projectId, gateInstanceId, studentId, ct);
            var existing = await db.Set<GateIndividualProcessScore>()
                .SingleOrDefaultAsync(s => s.GateInstanceId == gateInstanceId && s.StudentId == studentId, ct)
                ?? throw new NotFoundException("GateIndividualProcessScore", $"{gateInstanceId}:{studentId}");

            if (existing.Status == "FINALIZED")
                throw new ConflictException("Individual process score is already finalized.", WorkflowErrorCodes.ImmutableVersion);
            if (!Guid.TryParse(input.ConcurrencyToken, out var parsed) || parsed != existing.ConcurrencyToken)
                throw new ConflictException("Individual process score has changed. Reload before finalizing.", WorkflowErrorCodes.StaleConcurrencyToken);

            var now = Now;
            existing.SupervisorAssignmentId = assignmentId;
            existing.Status = "FINALIZED";
            existing.FinalizedAt = now;
            existing.UpdatedAt = now;
            existing.ConcurrencyToken = Guid.NewGuid();
            await db.SaveChangesAsync(ct);

            await audit.RecordAsync(new(Actor, "GATE_INDIVIDUAL_PROCESS_SCORE_FINALIZED", "GATE_INDIVIDUAL_PROCESS_SCORE", existing.Id,
                new Dictionary<string, object?> { ["projectId"] = projectId, ["gateNumber"] = gateNumber, ["studentId"] = studentId, ["score"] = existing.Score }), ct);
            return Map(existing, gateNumber, false);
        }, ct);

    public Task<GateIndividualProcessScoreItemDto> SaveMentorReferenceNoteAsync(
        long projectId, long gateInstanceId, long studentId, UpsertMentorReferenceNoteRequest input, CancellationToken ct) =>
        transactions.InTransactionAsync(async () =>
        {
            await transactions.LockProjectAsync(projectId, ct);
            if (string.IsNullOrWhiteSpace(input.MentorReferenceNotes) || input.MentorReferenceNotes.Trim().Length > 2000)
                throw new ValidationException(new Dictionary<string, string[]> { ["mentorReferenceNotes"] = ["Reference notes must be 1..2000 characters."] });

            var project = await db.Projects.AsNoTracking()
                .Where(p => p.Id == projectId)
                .Select(p => new { p.Id, p.TeamId, p.Status })
                .SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Project", projectId);
            if (project.Status is not ("ACTIVE" or "FINAL_SUBMISSION"))
                throw new ConflictException("Project is not open for gate process scoring.", WorkflowErrorCodes.ProjectNotActive);

            var student = await db.TeamMembers.AsNoTracking()
                .Where(m => m.TeamId == project.TeamId && m.UserId == studentId && m.LeftAt == null)
                .Select(m => new { m.UserId, m.User.MajorId })
                .SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Student", studentId);

            var mentorMajors = await db.SupervisorAssignments.AsNoTracking()
                .Where(a => a.ProjectId == projectId && a.EndedAt == null && a.AssignmentType == "DISCIPLINE_MENTOR"
                    && a.MajorId != null && a.SupervisorProfile.UserId == Actor)
                .Select(a => a.MajorId!.Value)
                .ToArrayAsync(ct);
            if (mentorMajors.Length == 0)
                throw new ForbiddenException("Only an active discipline mentor can submit advisory reference notes.");
            if (!student.MajorId.HasValue || !mentorMajors.Contains(student.MajorId.Value))
                throw new ForbiddenException("Mentor cannot access students outside their assigned major scope.", WorkflowErrorCodes.ScopeAccessDenied);

            var gate = await db.ProjectGateInstances.AsNoTracking()
                .Where(g => g.Id == gateInstanceId && g.ProjectId == projectId)
                .Select(g => new { g.Id, g.Sequence })
                .SingleOrDefaultAsync(ct) ?? throw new NotFoundException("ProjectGateInstance", gateInstanceId);

            var existing = await db.Set<GateIndividualProcessScore>()
                .SingleOrDefaultAsync(s => s.GateInstanceId == gateInstanceId && s.StudentId == studentId, ct)
                ?? throw new ConflictException("Primary supervisor has not initialized the draft score for this gate yet.", "INDIVIDUAL_PROCESS_DRAFT_NOT_INITIALIZED");
            if (existing.Status == "FINALIZED")
                throw new ConflictException("Cannot add mentor reference notes after the primary supervisor has finalized this gate score.", WorkflowErrorCodes.ImmutableVersion);
            if (!string.IsNullOrWhiteSpace(input.ConcurrencyToken)
                && (!Guid.TryParse(input.ConcurrencyToken, out var parsed) || parsed != existing.ConcurrencyToken))
                throw new ConflictException("Individual process score has changed. Reload before retrying.", WorkflowErrorCodes.StaleConcurrencyToken);

            existing.MentorReferenceNotes = input.MentorReferenceNotes.Trim();
            existing.UpdatedAt = Now;
            existing.ConcurrencyToken = Guid.NewGuid();
            await db.SaveChangesAsync(ct);

            await audit.RecordAsync(new(Actor, "GATE_INDIVIDUAL_PROCESS_MENTOR_NOTE_SAVED", "GATE_INDIVIDUAL_PROCESS_SCORE", existing.Id,
                new Dictionary<string, object?> { ["projectId"] = projectId, ["gateNumber"] = gate.Sequence, ["studentId"] = studentId }), ct);
            return Map(existing, gate.Sequence, false);
        }, ct);

    private async Task<(long AssignmentId, int GateNumber, decimal GateWeight)> ValidatePrimaryWriteContextAsync(
        long projectId, long gateInstanceId, long studentId, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new { p.Id, p.TeamId, p.Status })
            .SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Project", projectId);

        var primaryAssignmentId = await db.SupervisorAssignments.AsNoTracking()
            .Where(a => a.ProjectId == projectId && a.EndedAt == null && a.IsPrimary && a.AssignmentType == "PRIMARY"
                && a.SupervisorProfile.UserId == Actor)
            .Select(a => (long?)a.Id)
            .SingleOrDefaultAsync(ct)
            ?? throw new ForbiddenException("Only the active Primary Supervisor can record or finalize 20% individual process scores (POL-03).", "PRIMARY_SUPERVISOR_REQUIRED");

        if (project.Status is not ("ACTIVE" or "FINAL_SUBMISSION"))
            throw new ConflictException("Individual process scoring requires an ACTIVE or FINAL_SUBMISSION project.", WorkflowErrorCodes.ProjectNotActive);

        var isMember = await db.TeamMembers.AsNoTracking().AnyAsync(m =>
            m.TeamId == project.TeamId && m.UserId == studentId && m.LeftAt == null, ct);
        if (!isMember) throw new NotFoundException("Student", studentId);

        var gate = await db.ProjectGateInstances.AsNoTracking()
            .Where(g => g.Id == gateInstanceId && g.ProjectId == projectId)
            .Select(g => new { g.Id, g.Sequence })
            .SingleOrDefaultAsync(ct) ?? throw new NotFoundException("ProjectGateInstance", gateInstanceId);

        var weights = await LoadGateWeightsAsync(projectId, ct);
        var gateWeight = weights.FirstOrDefault(w => w.GateNumber == gate.Sequence)?.WeightPercent
            ?? throw new ConflictException($"Gate G{gate.Sequence} is not configured in the individual process gate weights.");

        return (primaryAssignmentId, gate.Sequence, gateWeight);
    }

    private static GateIndividualProcessScoreItemDto Map(GateIndividualProcessScore row, int gateNumber, bool hideInternalNotes) =>
        new(row.Id, row.GateInstanceId, gateNumber, row.StudentId, row.SupervisorAssignmentId,
            row.Score, row.GateWeightPercent,
            hideInternalNotes ? null : row.MentorReferenceNotes,
            row.Comments, row.Status, row.ConcurrencyToken.ToString("N"),
            DateTime.SpecifyKind(row.UpdatedAt, DateTimeKind.Utc),
            row.FinalizedAt.HasValue ? DateTime.SpecifyKind(row.FinalizedAt.Value, DateTimeKind.Utc) : null);
}
