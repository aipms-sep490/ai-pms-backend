using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Appeals;
using AIPMS.Application.Features.Results.DTOs;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed partial class ResultAppealService
{
    private static void Pending(ResultAppealRequest row)
    { if (row.Status is not ("PENDING" or "UNDER_REVIEW")) throw new ConflictException("The decision is immutable.", "APPEAL_CLOSED"); }
    public Task<CorrectionRequestDto> Assign(long projectId, long id, AssignAppealReviewerRequest input, CancellationToken ct) => transactions.InTransactionAsync(async () => {
        await transactions.LockProjectAsync(projectId, ct); var access = await Scope(projectId, ct); Lead(access);
        var row = await Row(projectId, id, ct); Token(row.ConcurrencyToken, input.ConcurrencyToken); Pending(row); await Current(row, ct);
        await Approved(projectId, row.PolicyId, ct); await Reviewer(row, input.ReviewerId, ct);
        row.ReviewerId = input.ReviewerId; row.Status = "UNDER_REVIEW"; row.Proposal = null; row.ReviewReason = null; row.BundleId = null;
        row.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync(ct); await Event(row, "APPEAL_REVIEWER_ASSIGNED", null, ct);
        return await Dto(row, access, ct);
    }, ct);
    public Task<CorrectionRequestDto> Review(long projectId, long id, ReviewAppealRequest input, CancellationToken ct) => transactions.InTransactionAsync(async () => {
        await transactions.LockProjectAsync(projectId, ct); var access = await Scope(projectId, ct); var row = await Row(projectId, id, ct);
        Token(row.ConcurrencyToken, input.ConcurrencyToken); Pending(row); await Current(row, ct); await Approved(projectId, row.PolicyId, ct);
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
    private async Task<ResultCorrectionBundle> Bundle(ResultAppealRequest row, CancellationToken ct) =>
        await db.Set<ResultCorrectionBundle>().Include(b => b.Amendments).SingleOrDefaultAsync(b => b.ProjectId == row.ProjectId && b.Id == row.BundleId, ct)
        ?? throw new ConflictException("A reviewed correction bundle is required.", "APPEAL_AMENDMENT_REQUIRED");
    public async Task<AppealReviewContextDto> ReviewContext(long projectId, long id, CancellationToken ct)
    {
        var access = await Scope(projectId, ct); var row = await Row(projectId, id, ct);
        if (!DepartmentAccess(row, access))
        { if (row.ReviewerId != Actor) throw new ForbiddenException(); await Reviewer(row, Actor, ct); }
        var frozen = Frozen(row);
        var amendments = row.BundleId.HasValue ? (await Bundle(row, ct)).Amendments.Select(a =>
            new ScoreAmendmentInput(a.EvaluationId, a.CriterionId, a.Score, a.Reason)).ToArray() : [];
        return new(row.Id, row.ComponentId, row.Scope, row.MajorId, row.StudentId, frozen.Scheme.Policy!.CalculationVersion,
            frozen.Evaluations.Select(e => new AppealReviewEvaluationDto(e.Id, e.AssignmentId, e.EvaluatorId, e.RubricId,
                e.RubricName, e.RubricVersion, e.TotalScore, e.Scores)).ToArray(), amendments);
    }
    public Task<AppealPreviewDto> Preview(long projectId, long id, CancellationToken ct) => transactions.InTransactionAsync(async () => {
        await transactions.LockProjectAsync(projectId, ct); var access = await Scope(projectId, ct); var row = await Row(projectId, id, ct);
        Pending(row); await Current(row, ct);
        Lead(access);
        if (row.ReviewerId == Actor || Scorers(row).Contains(Actor)) throw new ForbiddenException("Preview requires an independent lead decision maker.");
        if (row.ReviewerId is not long reviewer || row.Proposal != "AMEND") throw new ConflictException("Reviewed amendments required.");
        await Reviewer(row, reviewer, ct); await Approved(projectId, row.PolicyId, ct);
        var check = await revisions.Check(await Bundle(row, ct), ct);
        // Cross-target totals are visible only to the lead orchestrator, never a component-only reviewer.
        return new AppealPreviewDto(check.ConfirmationToken, check.Targets.Select(t => new ResultRevisionSummary(null,
            t.Current.Version+1, t.Current.Version, t.Current.Key == 0 ? null : t.Current.Key, t.Calculation.TotalScore,
            "PREVIEW", Actor, Now, null, row.BundleId)).ToArray());
    }, ct);
    public Task<CorrectionRequestDto> Decide(long projectId, long id, AppealDecisionRequest input, CancellationToken ct) => transactions.InTransactionAsync(async () => {
        await transactions.LockProjectAsync(projectId, ct); var access = await Scope(projectId, ct); Lead(access);
        var row = await Row(projectId, id, ct); Token(row.ConcurrencyToken, input.ConcurrencyToken); Pending(row);
        await Approved(projectId, row.PolicyId, ct); var reason = Reason(input.Reason);
        if (row.ReviewerId == Actor || Scorers(row).Contains(Actor)) throw new ForbiddenException("Decision maker must be independent of the reviewer and original scorer.");
        if (input.Decision == "ACCEPTED")
        {
            await Current(row, ct);
            if (row.ReviewerId is not long reviewer || row.Proposal != "AMEND") throw new ConflictException("Acceptance requires a reviewed correction.");
            await Reviewer(row, reviewer, ct);
            await revisions.Publish(await Bundle(row, ct), Actor, input.ConfirmationToken ?? "", ct);
            effective = null;
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
        var rows = await query.OrderByDescending(e => e.Id).Skip((page-1)*pageSize).Take(pageSize).ToArrayAsync(ct);
        var reviewerView = DepartmentAccess(row, access) || row.ReviewerId == Actor && EligibleReviewer(row, access);
        var bundleIds = reviewerView ? rows.Where(e => e.BundleId.HasValue).Select(e => e.BundleId!.Value).Distinct().ToArray() : [];
        var amendments = await db.Set<ResultScoreAmendment>().AsNoTracking().Where(a => bundleIds.Contains(a.BundleId)).ToArrayAsync(ct);
        return new(rows.Select(e => new AppealEventDto(e.Id, e.Action, e.ActorId, Utc(e.At), e.Reason,
            reviewerView ? e.BundleId : null, amendments.Where(a => a.BundleId == e.BundleId)
                .Select(a => new ScoreAmendmentInput(a.EvaluationId, a.CriterionId, a.Score, a.Reason)).ToArray())).ToArray(), page, pageSize, count);
    }
}
