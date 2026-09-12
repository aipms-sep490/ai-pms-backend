using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.FinalSubmissions.Abstractions;
using AIPMS.Application.Features.FinalSubmissions.DTOs;
using AIPMS.Application.Features.FinalSubmissions.Models;
using AIPMS.Application.Features.Supervisors.Abstractions;

namespace AIPMS.Application.Features.FinalSubmissions.Services;

public sealed class FinalSubmissionDraftWorkflow(IFinalSubmissionDraftRepository repository, IFinalSubmissionRepository submissions,
    ISupervisorProfileRepository accounts, ICurrentUser currentUser, IAuditTrail audit, TimeProvider clock)
{
    private long ActorId => currentUser.IsAuthenticated && currentUser.UserId is long id
        ? id : throw new UnauthorizedException();

    private async Task<FinalDraftProject> Authorize(long projectId, long actorId, CancellationToken ct)
    {
        var actor = await accounts.GetAccountAsync(actorId, ct);
        if (actor is null || !actor.IsActive || !actor.HasActiveAcademicScope || !actor.Roles.Contains(AppRoles.Student))
            throw new ForbiddenException("Only active student team members can access a final-submission draft.");
        var project = await repository.GetProjectAsync(projectId, actorId, ct)
            ?? throw new NotFoundException("Project", projectId);
        if (!project.IsMember) throw new ForbiddenException("You are not a current member of this project team.");
        return project;
    }

    public Task<FinalSubmissionDraftDto> Get(long projectId, CancellationToken ct) => repository.InTransactionAsync(async () =>
    {
        var project = await Authorize(projectId, ActorId, ct);
        var draft = await repository.GetAsync(projectId, ct) ?? throw new NotFoundException("FinalSubmissionDraft", projectId);
        return await Detail(draft, project, ct);
    }, ct);

    public Task<PagedResult<FinalSubmissionPeriodOptionDto>> Periods(long projectId, int page, int size, CancellationToken ct) =>
        repository.InTransactionAsync(async () =>
        {
            var project = await Authorize(projectId, ActorId, ct);
            var now = clock.GetUtcNow().UtcDateTime;
            var periods = await repository.GetPeriodsAsync(project.SemesterId, now, page, size, ct);
            var openCount = await repository.CountOpenPeriodsAsync(project.SemesterId, now, ct);
            var locked = await submissions.GetAsync(projectId, ct) is not null;
            var items = new List<FinalSubmissionPeriodOptionDto>();
            foreach (var period in periods.Items)
            {
                var blockers = (await FinalSubmissionRules.Blockers(repository, project, period, now, ct, openCount)).ToList();
                if (locked) blockers.Add("ALREADY_SUBMITTED");
                items.Add(new(period.Id, period.Name, period.Status, period.StartAt, period.EndAt, blockers.Count == 0, blockers));
            }
            return new PagedResult<FinalSubmissionPeriodOptionDto>(items, page, size, periods.TotalCount);
        }, ct);

    public Task<FinalSubmissionDraftDto> Create(long projectId, CreateFinalSubmissionDraftRequest input, CancellationToken ct) =>
        Save(projectId, input.ProjectPeriodId, input.Notes, input.DeliverableVersionIds, null, ct);

    public Task<FinalSubmissionDraftDto> Update(long projectId, UpdateFinalSubmissionDraftRequest input, CancellationToken ct) =>
        Save(projectId, input.ProjectPeriodId, input.Notes, input.DeliverableVersionIds, input.ConcurrencyToken, ct);

    private Task<FinalSubmissionDraftDto> Save(long projectId, long periodId, string? notes,
        IReadOnlyList<long> versionIds, string? expectedToken, CancellationToken ct) => repository.InTransactionAsync(async () =>
    {
        var actor = ActorId;
        // Use the same project lock boundary as BE-08 before reading mutable file/version state.
        await repository.LockProjectAsync(projectId, ct);
        var project = await Authorize(projectId, actor, ct);
        if (!project.IsLeader) throw new ForbiddenException("Only the current student leader can prepare the final package.");
        if (await submissions.GetAsync(projectId, ct) is not null) throw new ConflictException("The final package is already locked.");
        var before = await repository.GetAsync(projectId, ct);
        if (expectedToken is null && before is not null) throw new ConflictException("A final-submission draft already exists for this project.");
        if (expectedToken is not null)
        {
            if (before is null) throw new NotFoundException("FinalSubmissionDraft", projectId);
            if (!Guid.TryParse(expectedToken, out var expected) || expected != Guid.Parse(before.ConcurrencyToken))
                throw new ConflictException("The draft changed. Reload before saving.");
        }
        var versions = await repository.GetVersionsAsync(projectId, versionIds, ct);
        if (versions.Count != versionIds.Count)
            throw new ConflictException("Every selected version must exist in this project.");
        if (versions.Select(v => v.DeliverableId).Distinct().Count() != versions.Count)
            throw new ConflictException("Select at most one version of each deliverable.");
        if (versions.Any(v => !v.FilesValid || v.Status is not ("SUBMITTED" or "ACCEPTED")))
            throw new ConflictException("Selected versions must be submitted or accepted and have valid BE-08 file metadata.");
        var now = clock.GetUtcNow().UtcDateTime;
        var period = await repository.GetPeriodAsync(periodId, now, ct);
        var blockers = await FinalSubmissionRules.Blockers(repository, project, period, now, ct);
        if (blockers.Count != 0) throw new ConflictException(string.Join(", ", blockers));
        var saved = await repository.SaveAsync(projectId, periodId, notes?.Trim(), versionIds, actor, now, ct);
        await audit.RecordAsync(new(actor, before is null ? "FINAL_SUBMISSION_DRAFT_CREATED" : "FINAL_SUBMISSION_DRAFT_UPDATED",
            "FINAL_SUBMISSION_DRAFT", saved.Id, new Dictionary<string, object?> { ["before"] = before, ["after"] = saved }), ct);
        // Recheck time after persistence/audit; a request that crosses the deadline must roll back.
        var result = await Detail(saved, project, ct);
        if (!result.CanEdit) throw new ConflictException(string.Join(", ", result.EditBlockers));
        return result;
    }, ct);

    private async Task<FinalSubmissionDraftDto> Detail(FinalDraftRecord draft, FinalDraftProject project, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var period = await repository.GetPeriodAsync(draft.ProjectPeriodId, now, ct);
        var versions = await repository.GetVersionsAsync(project.Id, draft.VersionIds, ct);
        if (versions.Count != draft.VersionIds.Count)
            throw new ConflictException("Draft version references no longer belong to this project.");
        var blockers = (await FinalSubmissionRules.Blockers(repository, project, period, now, ct)).ToList();
        var locked = await submissions.GetAsync(project.Id, ct) is not null;
        if (locked) blockers.Add("ALREADY_SUBMITTED");
        return draft.ToDto(period, blockers, versions) with { IsLocked = locked, Status = locked ? "LOCKED" : "DRAFT" };
    }
}
