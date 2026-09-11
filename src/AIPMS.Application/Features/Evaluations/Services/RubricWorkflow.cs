using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;

namespace AIPMS.Application.Features.Evaluations.Services;

public sealed class RubricWorkflow(IRubricRepository repository, ICurrentUser currentUser,
    IAuditTrail audit, TimeProvider clock)
{
    private async Task<RubricActor> Actor(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null) throw new UnauthorizedException();
        return await repository.GetActorAsync(currentUser.UserId.Value, ct)
            ?? throw new ForbiddenException("Only active administrators or department staff can manage rubrics.");
    }

    private async Task Scope(RubricActor actor, long department, long semester, CancellationToken ct)
    {
        if (!actor.IsAdmin && actor.DepartmentId != department) throw new ForbiddenException();
        var scope = await repository.GetScopeAsync(department, semester, ct)
            ?? throw new ConflictException("Rubric department and semester must belong to the same active organization and department.");
        if (scope.SemesterStatus is "CLOSED" or "ARCHIVED")
            throw new ConflictException("Rubrics cannot be changed in a closed or archived semester.");
    }

    private async Task<RubricRecord> Load(long id, RubricActor actor, bool forUpdate, CancellationToken ct) =>
        await repository.GetAsync(id, actor, forUpdate, ct) ?? throw new NotFoundException("Rubric", id);

    private Task Audit(string action, RubricRecord rubric, long actor, CancellationToken ct) =>
        audit.RecordAsync(new AuditEntry(actor, action, "RUBRIC", rubric.Id,
            new Dictionary<string, object?> { ["rootRubricId"] = rubric.RootRubricId,
                ["version"] = rubric.Version, ["status"] = rubric.Status }), ct);

    public async Task<PagedResult<RubricDto>> List(RubricFilter filter, CancellationToken ct)
    {
        var actor = await Actor(ct);
        if (!actor.IsAdmin && filter.DepartmentId.HasValue && filter.DepartmentId != actor.DepartmentId)
            throw new ForbiddenException();
        var result = await repository.ListAsync(actor, filter, ct);
        return new(result.Items.Select(r => r.ToDto()).ToArray(), result.Page, result.PageSize, result.TotalCount);
    }

    public async Task<RubricDto> Get(long id, CancellationToken ct) => (await Load(id, await Actor(ct), false, ct)).ToDto();

    public Task<RubricDto> Create(CreateRubricRequest input, CancellationToken ct) => repository.InTransactionAsync(async () =>
    {
        var actor = await Actor(ct);
        await Scope(actor, input.DepartmentId, input.AcademicSemesterId, ct);
        var rubric = await repository.CreateAsync(input.DepartmentId, input.AcademicSemesterId,
            input.Code.Trim().ToUpperInvariant(), input.Name.Trim(), input.Description?.Trim(), input.Criteria,
            actor.UserId, clock.GetUtcNow().UtcDateTime, null, ct);
        await Audit("RUBRIC_CREATED", rubric, actor.UserId, ct);
        return rubric.ToDto();
    }, ct);

    public Task<RubricDto> Update(long id, UpdateRubricRequest input, CancellationToken ct) => repository.InTransactionAsync(async () =>
    {
        var actor = await Actor(ct);
        var rubric = await Load(id, actor, true, ct);
        RubricRules.EnsureCurrent(rubric, input.ConcurrencyToken);
        RubricRules.EnsureEditable(rubric);
        await Scope(actor, rubric.DepartmentId!.Value, rubric.AcademicSemesterId!.Value, ct);
        var updated = await repository.UpdateAsync(id, input.Name.Trim(), input.Description?.Trim(),
            input.Criteria, clock.GetUtcNow().UtcDateTime, ct);
        await Audit("RUBRIC_UPDATED", updated, actor.UserId, ct);
        return updated.ToDto();
    }, ct);

    public Task<RubricDto> ChangeStatus(long id, string token, bool publish, CancellationToken ct) => repository.InTransactionAsync(async () =>
    {
        var actor = await Actor(ct);
        var rubric = await Load(id, actor, true, ct);
        RubricRules.EnsureCurrent(rubric, token);
        if (rubric.DepartmentId is null || rubric.AcademicSemesterId is null)
            throw new ConflictException("Legacy unscoped rubrics are read-only; create a scoped rubric instead.");
        await Scope(actor, rubric.DepartmentId.Value, rubric.AcademicSemesterId.Value, ct);
        if (publish)
        {
            RubricRules.EnsureEditable(rubric);
            RubricRules.EnsurePublishable(rubric.Criteria);
        }
        else if (rubric.Status != RubricStatuses.Published)
            throw new ConflictException("Only published rubrics can be retired.");
        var updated = await repository.SetStatusAsync(id, publish ? RubricStatuses.Published : RubricStatuses.Retired,
            clock.GetUtcNow().UtcDateTime, ct);
        await Audit(publish ? "RUBRIC_PUBLISHED" : "RUBRIC_RETIRED", updated, actor.UserId, ct);
        return updated.ToDto();
    }, ct);

    public Task<RubricDto> NewVersion(long id, CreateRubricVersionRequest input, CancellationToken ct) => repository.InTransactionAsync(async () =>
    {
        var actor = await Actor(ct);
        var rubric = await Load(id, actor, true, ct);
        RubricRules.EnsureCurrent(rubric, input.ConcurrencyToken);
        if (rubric.Status == RubricStatuses.Draft || rubric.DepartmentId is null || rubric.AcademicSemesterId is null)
            throw new ConflictException("Create a new version from a published or retired, academically scoped rubric.");
        await Scope(actor, rubric.DepartmentId.Value, rubric.AcademicSemesterId.Value, ct);
        var created = await repository.CreateAsync(rubric.DepartmentId.Value, rubric.AcademicSemesterId.Value,
            input.Code.Trim().ToUpperInvariant(), rubric.Name, rubric.Description,
            rubric.Criteria.Select(c => new RubricCriterionInput(c.Name, c.Description, c.WeightPercent,
                c.MaxScore, c.SortOrder, c.IsRequired)).ToArray(), actor.UserId, clock.GetUtcNow().UtcDateTime, id, ct);
        await Audit("RUBRIC_VERSION_CREATED", created, actor.UserId, ct);
        return created.ToDto();
    }, ct);

    public Task<bool> Delete(long id, string token, CancellationToken ct) => repository.InTransactionAsync(async () =>
    {
        var actor = await Actor(ct);
        var rubric = await Load(id, actor, true, ct);
        RubricRules.EnsureCurrent(rubric, token);
        RubricRules.EnsureEditable(rubric);
        await Scope(actor, rubric.DepartmentId!.Value, rubric.AcademicSemesterId!.Value, ct);
        await repository.DeleteAsync(id, ct);
        await Audit("RUBRIC_DELETED", rubric, actor.UserId, ct);
        return true;
    }, ct);
}
