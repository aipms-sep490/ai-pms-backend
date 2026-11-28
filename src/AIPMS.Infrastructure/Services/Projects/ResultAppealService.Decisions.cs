using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Appeals;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Services;
using AIPMS.Application.Features.Results.DTOs;
using AIPMS.Domain.Exceptions;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed partial class ResultAppealService
{
    public Task<CorrectionRequestDto> Withdraw(long projectId, long id, WithdrawAppealRequest input, CancellationToken ct) => transactions.InTransactionAsync(async () =>
    {
        await transactions.LockProjectAsync(projectId, ct);
        var access = await Scope(projectId, ct);
        var row = await Row(projectId, id, ct);
        if (row.Source != "STUDENT_APPEAL" || row.RequestedBy != Actor || !access.Student)
            throw new ForbiddenException("Only the student who submitted the appeal can withdraw it.");
        Token(row.ConcurrencyToken, input.ConcurrencyToken);
        if (row.Status != "PENDING")
            throw new ConflictException("The appeal is already under review and cannot be withdrawn.", "APPEAL_NOT_PENDING");
        var reason = Reason(input.Reason);
        row.Status = "WITHDRAWN";
        row.DecisionReason = reason;
        row.DecidedBy = Actor;
        row.DecidedAt = Now;
        row.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        await Event(row, "WITHDRAW_REQUEST", reason, ct);
        return await Dto(row, access, ct);
    }, ct);

    private static void Pending(ResultAppealRequest row)
    { if (row.Status is not ("PENDING" or "UNDER_REVIEW")) throw new ConflictException("The decision is immutable.", "APPEAL_CLOSED"); }

    public async Task<IReadOnlyList<EligibleAppealReviewerDto>> GetEligibleReviewers(long projectId, long id, CancellationToken ct)
    {
        var access = await Scope(projectId, ct);
        var row = await Row(projectId, id, ct);
        if (!DepartmentAccess(row, access)) throw new ForbiddenException();
        if (row.AppealKind != "INDEPENDENT_80") return [];

        var originalScorers = Scorers(row).ToHashSet();
        var candidates = await db.Users.AsNoTracking()
            .Where(u => u.Status == "ACTIVE"
                && u.DepartmentId == row.ResponsibleDepartmentId
                && u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive
                && u.Id != row.StudentId
                && u.UserRoleUsers.Any(r => r.Role.Code == "LECTURER" || r.Role.Code == "DEPARTMENT_STAFF"))
            .Select(u => new
            {
                u.Id,
                u.FullName,
                u.Email,
                DepartmentId = u.DepartmentId!.Value,
                DepartmentName = u.Department!.Name
            })
            .OrderBy(u => u.FullName)
            .ToArrayAsync(ct);

        var candidateIds = candidates.Select(c => c.Id).ToArray();
        var coiIds = await AcademicAssignmentGuard.GetConflictingSupervisorOrMentorUserIdsAsync(db, projectId, candidateIds, ct);

        return candidates
            .Where(c => !coiIds.Contains(c.Id) && !originalScorers.Contains(c.Id))
            .Select(c => new EligibleAppealReviewerDto(c.Id, c.FullName, c.Email, c.DepartmentId, c.DepartmentName))
            .ToArray();
    }

    public Task<CorrectionRequestDto> Assign(long projectId, long id, AssignAppealReviewerRequest input, CancellationToken ct) => transactions.InTransactionAsync(async () =>
    {
        await transactions.LockProjectAsync(projectId, ct); var access = await Scope(projectId, ct); Lead(access);
        var row = await Row(projectId, id, ct); Token(row.ConcurrencyToken, input.ConcurrencyToken); Pending(row); await Current(row, ct);
        if (row.AppealKind != "INDEPENDENT_80")
            throw new ConflictException("Only INDEPENDENT_80 appeals can be assigned to an external appeal reviewer (QD-04).",
                WorkflowErrorCodes.AppealProcess20NoExternalReviewer);
        await Approved(projectId, row.PolicyId, ct); await Reviewer(row, input.ReviewerId, ct);
        row.ReviewerId = input.ReviewerId; row.Status = "UNDER_REVIEW"; row.Proposal = null; row.ReviewReason = null; row.BundleId = null;
        row.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync(ct); await Event(row, "APPEAL_REVIEWER_ASSIGNED", null, ct);
        return await Dto(row, access, ct);
    }, ct);

    public Task<CorrectionRequestDto> Review(long projectId, long id, ReviewAppealRequest input, CancellationToken ct) => transactions.InTransactionAsync(async () =>
    {
        await transactions.LockProjectAsync(projectId, ct); var access = await Scope(projectId, ct); var row = await Row(projectId, id, ct);
        Token(row.ConcurrencyToken, input.ConcurrencyToken); Pending(row); await Current(row, ct); await Approved(projectId, row.PolicyId, ct);
        if (row.AppealKind != "INDEPENDENT_80")
            throw new ConflictException("External reviewer scoring applies only to INDEPENDENT_80 appeals.", WorkflowErrorCodes.AppealProcess20NoExternalReviewer);
        if (row.ReviewerId != Actor || row.Status != "UNDER_REVIEW") throw new ForbiddenException();
        await Reviewer(row, Actor, ct); var reason = Reason(input.Reason);
        if (input.Proposal is not ("KEEP" or "AMEND") || input.Amendments is null
            || input.Proposal == "KEEP" && input.Amendments.Count != 0 || input.Proposal == "AMEND" && input.Amendments.Count == 0)
            throw Invalid("KEEP has no amendments; AMEND requires criterion amendments.");
        var frozen = Frozen(row);
        if (input.Amendments.Any(a => a is null || frozen.Evaluations.All(e => e.Id != a.EvaluationId)))
            throw new ForbiddenException("Amendments must belong to the assigned component and target.");
        row.BundleId = null;
        if (input.Proposal == "AMEND")
        {
            var bundle = await revisions.Create(projectId, Actor, reason, input.Amendments, ct); row.BundleId = bundle.Id;
        }
        row.Proposal = input.Proposal; row.ReviewReason = reason; row.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct); await Event(row, "APPEAL_REVIEWED", reason, ct);
        return await Dto(row, access, ct);
    }, ct);

    public Task<CorrectionRequestDto> SubmitSupervisorErrata(long projectId, long id, SupervisorErrataResponseRequest input, CancellationToken ct) => transactions.InTransactionAsync(async () =>
    {
        await transactions.LockProjectAsync(projectId, ct);
        var access = await Scope(projectId, ct);
        if (!access.ActivePrimarySupervisor)
            throw new ForbiddenException("Only the active Primary Supervisor of this project may respond to 20% process errata appeals (QD-04).");
        var row = await Row(projectId, id, ct);
        Token(row.ConcurrencyToken, input.ConcurrencyToken);
        Pending(row);
        await Current(row, ct);
        await Approved(projectId, row.PolicyId, ct);
        if (row.AppealKind != "PROCESS_20_ERRATA" || !row.StudentId.HasValue)
            throw new ConflictException("Supervisor errata response applies only to PROCESS_20_ERRATA appeals.", WorkflowErrorCodes.AppealProcess20NoExternalReviewer);

        var reason = Reason(input.Reason);
        if (input.Proposal is not ("KEEP" or "AMEND") || input.GateAmendments is null
            || input.Proposal == "KEEP" && input.GateAmendments.Count != 0
            || input.Proposal == "AMEND" && input.GateAmendments.Count == 0)
            throw Invalid("KEEP has no gate amendments; AMEND requires 1..3 gate score amendments.");

        row.BundleId = null;
        if (input.Proposal == "AMEND")
        {
            var bundle = await revisions.CreateGateErrata(projectId, row.StudentId.Value, Actor, reason, input.GateAmendments, ct);
            row.BundleId = bundle.Id;
        }
        row.Status = "UNDER_REVIEW";
        row.Proposal = input.Proposal;
        row.ReviewReason = reason;
        row.SupervisorNote = reason;
        row.SupervisorRespondedBy = Actor;
        row.SupervisorRespondedAt = Now;
        row.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        await Event(row, "APPEAL_SUPERVISOR_ERRATA", reason, ct);
        return await Dto(row, access, ct);
    }, ct);

    private async Task<ResultCorrectionBundle> Bundle(ResultAppealRequest row, CancellationToken ct) =>
        await db.Set<ResultCorrectionBundle>()
            .Include(b => b.Amendments)
            .Include(b => b.GateAmendments)
            .SingleOrDefaultAsync(b => b.ProjectId == row.ProjectId && b.Id == row.BundleId, ct)
        ?? throw new ConflictException("A reviewed correction bundle is required.", "APPEAL_AMENDMENT_REQUIRED");

    public async Task<AppealReviewContextDto> ReviewContext(long projectId, long id, CancellationToken ct)
    {
        var access = await Scope(projectId, ct); var row = await Row(projectId, id, ct);
        if (!DepartmentAccess(row, access) && !(row.AppealKind == "PROCESS_20_ERRATA" && access.ActivePrimarySupervisor))
        { if (row.ReviewerId != Actor) throw new ForbiddenException(); await Reviewer(row, Actor, ct); }
        var frozen = Frozen(row);
        var evaluatorIds = frozen.Evaluations.Select(e => e.EvaluatorId).Distinct().ToArray();
        var evaluatorNames = evaluatorIds.Length == 0 ? new Dictionary<long, string>()
            : await db.Users.AsNoTracking().Where(u => evaluatorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var criterionNames = frozen.Evaluations.SelectMany(e => e.Scores)
            .GroupBy(s => s.RubricCriterionId).ToDictionary(g => g.Key, g => g.First().Name);
        var bundle = row.BundleId.HasValue ? await Bundle(row, ct) : null;
        var amendments = bundle is not null ? bundle.Amendments.Select(a =>
            new ScoreAmendmentInput(a.EvaluationId, a.CriterionId, a.Score, a.Reason,
                criterionNames.GetValueOrDefault(a.CriterionId))).ToArray() : [];
        var gateAmendments = bundle is not null ? bundle.GateAmendments.Select(a =>
            new GateProcessAmendmentInput(a.GateScoreId, a.GateNumber, a.Score, a.Reason)).ToArray() : [];
        return new(row.Id, row.ComponentId, row.Scope, row.MajorId, row.StudentId, frozen.Scheme.Policy!.CalculationVersion,
            frozen.Evaluations.Select(e => new AppealReviewEvaluationDto(e.Id, e.AssignmentId, e.EvaluatorId, e.RubricId,
                e.RubricName, e.RubricVersion, e.TotalScore, e.Scores,
                evaluatorNames.GetValueOrDefault(e.EvaluatorId))).ToArray(),
            amendments,
            row.AppealKind,
            ComponentIdsOf(row),
            frozen.GateScores,
            gateAmendments);
    }

    public Task<AppealPreviewDto> Preview(long projectId, long id, CancellationToken ct) => transactions.InTransactionAsync(async () =>
    {
        await transactions.LockProjectAsync(projectId, ct); var access = await Scope(projectId, ct); var row = await Row(projectId, id, ct);
        Pending(row); await Current(row, ct);
        Lead(access);
        if (row.AppealKind == "INDEPENDENT_80")
        {
            if (row.ReviewerId == Actor || Scorers(row).Contains(Actor)) throw new ForbiddenException("Preview requires an independent lead decision maker.");
            if (row.ReviewerId is not long reviewer || row.Proposal != "AMEND") throw new ConflictException("Reviewed amendments required.");
            await Reviewer(row, reviewer, ct);
        }
        else if (row.AppealKind == "PROCESS_20_ERRATA")
        {
            if (row.Proposal != "AMEND" || !row.BundleId.HasValue) throw new ConflictException("Supervisor errata amendments required.");
        }
        else
        {
            throw new ConflictException("Preview with bundle applies to INDEPENDENT_80 or PROCESS_20_ERRATA appeals.");
        }
        await Approved(projectId, row.PolicyId, ct);
        var check = await revisions.Check(await Bundle(row, ct), ct, row.StudentId);
        var userIds = check.Targets.Where(t => t.Current.Key != 0).Select(t => t.Current.Key).Append(Actor).Distinct().ToArray();
        var userNames = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var actorName = userNames.GetValueOrDefault(Actor);
        // Cross-target totals are visible only to the lead orchestrator, never a component-only reviewer.
        return new AppealPreviewDto(check.ConfirmationToken, check.Targets.Select(t => new ResultRevisionSummary(null,
            t.Current.Version + 1, t.Current.Version, t.Current.Key == 0 ? null : t.Current.Key, t.Calculation.TotalScore,
            "PREVIEW", Actor, Now, null, row.BundleId, actorName,
            t.Current.Key == 0 ? null : userNames.GetValueOrDefault(t.Current.Key))).ToArray());
    }, ct);

    private async Task<IReadOnlyList<DefenseRecoveredScoreInput>> ResolveRecoveredDefenseScoresAsync(
        ResultAppealRequest row,
        IReadOnlyList<DefenseRecoveredScoreInput>? explicitScores,
        CancellationToken ct)
    {
        if (explicitScores is { Count: > 0 }) return explicitScores;

        using var root = JsonDocument.Parse(row.FrozenInputs);
        var scheme = root.RootElement.GetProperty("Scheme").Deserialize<EvaluationSchemeDto>()!;
        var student = scheme.Students.Single(s => s.StudentId == row.StudentId!.Value);
        var defenseComponents = scheme.Components
            .Where(c => c.Stage == "DEFENSE" && c.StudentWeightPercent > 0 && (c.Scope == "COMMON" || c.MajorId == student.MajorId))
            .ToArray();

        var projectTargets = await Effective(row.ProjectId, ct);
        var projectTarget = projectTargets.SingleOrDefault(t => t.Key == 0);
        var recovered = new List<DefenseRecoveredScoreInput>();
        if (projectTarget is not null)
        {
            using var projRoot = JsonDocument.Parse(projectTarget.Inputs);
            var projBreakdown = projRoot.RootElement.GetProperty("Breakdown").Deserialize<ResultBreakdownDto>();
            if (projBreakdown is not null)
            {
                foreach (var dc in defenseComponents)
                {
                    var match = projBreakdown.Components.FirstOrDefault(c => c.ComponentId == dc.Id && c.Score.HasValue);
                    if (match is not null) recovered.Add(new(dc.Id, match.Score!.Value));
                }
            }
        }
        return recovered;
    }

    public Task<CorrectionRequestDto> Decide(long projectId, long id, AppealDecisionRequest input, CancellationToken ct) => transactions.InTransactionAsync(async () =>
    {
        await transactions.LockProjectAsync(projectId, ct); var access = await Scope(projectId, ct); Lead(access);
        var row = await Row(projectId, id, ct); Token(row.ConcurrencyToken, input.ConcurrencyToken); Pending(row);
        await Approved(projectId, row.PolicyId, ct); var reason = Reason(input.Reason);
        if (row.AppealKind == "INDEPENDENT_80" && (row.ReviewerId == Actor || Scorers(row).Contains(Actor)))
            throw new ForbiddenException("Decision maker must be independent of the reviewer and original scorer.");

        if (input.Decision == "ACCEPTED")
        {
            await Current(row, ct);
            if (row.AppealKind == "INDEPENDENT_80")
            {
                if (row.ReviewerId is not long reviewer || row.Proposal != "AMEND") throw new ConflictException("Acceptance requires a reviewed correction.");
                await Reviewer(row, reviewer, ct);
                await revisions.Publish(await Bundle(row, ct), Actor, input.ConfirmationToken ?? "", ct);
                effective = null;
            }
            else if (row.AppealKind == "PROCESS_20_ERRATA")
            {
                if (row.Proposal != "AMEND" || !row.BundleId.HasValue)
                    throw new ConflictException("Acceptance of PROCESS_20_ERRATA requires Primary Supervisor errata amendments.");
                await revisions.Publish(await Bundle(row, ct), Actor, input.ConfirmationToken ?? "", ct, row.StudentId);
                effective = null;
            }
            else if (row.AppealKind == "DEFENSE_ABSENT_CONTEST")
            {
                if (!row.StudentId.HasValue) throw Invalid("Student target required for DEFENSE_ABSENT_CONTEST.");
                var absence = await db.Set<StudentDefenseAbsence>()
                    .SingleOrDefaultAsync(a => a.ProjectId == projectId && a.StudentId == row.StudentId.Value && a.RevokedAt == null, ct)
                    ?? throw new DomainException("No active DEFENSE_ABSENT record exists for this student.", WorkflowErrorCodes.DefenseAbsenceNotFound);

                var recovered = await ResolveRecoveredDefenseScoresAsync(row, input.RecoveredDefenseScores, ct);
                var overrideInput = new DefenseAbsenceAmendmentInput(absence.Id, true, reason, recovered);
                var bundleMaker = row.RequestedBy != Actor ? row.RequestedBy : row.StudentId.Value;
                var bundle = await revisions.CreateAbsenceOverride(projectId, row.StudentId.Value, bundleMaker, reason, overrideInput, ct);
                var check = await revisions.Check(bundle, ct, row.StudentId.Value);
                await revisions.Publish(bundle, Actor, check.ConfirmationToken, ct, row.StudentId.Value);

                absence.RevokedBy = Actor;
                absence.RevokedAt = Now;
                absence.RevokedReason = reason;
                absence.RevokedByAppealId = row.Id;
                row.BundleId = bundle.Id;
                row.Proposal = "AMEND";
                row.ReviewReason = reason;
                effective = null;
            }
        }
        else if (input.Decision != "REJECTED") throw Invalid("Decision must be ACCEPTED or REJECTED.");

        // A stale request can be explicitly rejected with a reason; it is never silently retargeted.
        row.Status = input.Decision; row.DecidedBy = Actor; row.DecidedAt = Now; row.DecisionReason = reason;
        row.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync(ct); await Event(row, "APPEAL_DECIDED", reason, ct);
        return await Dto(row, access, ct);
    }, ct);

    public async Task<PagedResult<AppealEventDto>> History(long projectId, long id, int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize); var access = await Scope(projectId, ct); var row = await Row(projectId, id, ct); await Read(row, access, ct);
        var query = db.Set<ResultAppealEvent>().AsNoTracking().Where(e => e.RequestId == id);
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(e => e.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        var reviewerView = DepartmentAccess(row, access)
            || (row.AppealKind == "PROCESS_20_ERRATA" && access.ActivePrimarySupervisor)
            || (row.ReviewerId == Actor && EligibleReviewer(row, access));
        var bundleIds = reviewerView ? rows.Where(e => e.BundleId.HasValue).Select(e => e.BundleId!.Value).Distinct().ToArray() : [];
        var amendments = await db.Set<ResultScoreAmendment>().AsNoTracking().Where(a => bundleIds.Contains(a.BundleId)).ToArrayAsync(ct);
        var actorIds = rows.Select(e => e.ActorId).Distinct().ToArray();
        var actorNames = actorIds.Length == 0 ? new Dictionary<long, string>()
            : await db.Users.AsNoTracking().Where(u => actorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var criterionNames = new Dictionary<long, string>();
        if (amendments.Length > 0)
        {
            try
            {
                foreach (var s in Frozen(row).Evaluations.SelectMany(e => e.Scores))
                    criterionNames.TryAdd(s.RubricCriterionId, s.Name);
            }
            catch { }
        }
        return new(rows.Select(e => new AppealEventDto(e.Id, e.Action, e.ActorId, Utc(e.At), e.Reason,
            reviewerView ? e.BundleId : null, amendments.Where(a => a.BundleId == e.BundleId)
                .Select(a => new ScoreAmendmentInput(a.EvaluationId, a.CriterionId, a.Score, a.Reason,
                    criterionNames.GetValueOrDefault(a.CriterionId))).ToArray(),
            actorNames.GetValueOrDefault(e.ActorId))).ToArray(), page, pageSize, count);
    }
}
