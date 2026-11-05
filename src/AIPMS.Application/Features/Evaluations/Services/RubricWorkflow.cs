using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Domain.Exceptions;
using Microsoft.Extensions.Options;

namespace AIPMS.Application.Features.Evaluations.Services;

public sealed class RubricWorkflow(IRubricRepository repository, ICurrentUser currentUser,
    IAuditTrail audit, TimeProvider clock, IOptions<RubricOptions>? rubricOptions = null)
{
    private readonly RubricOptions options = rubricOptions?.Value ?? new RubricOptions();

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

    private async Task ValidateApprovalScopeAuthority(RubricActor actor, RubricRecord rubric, CancellationToken ct)
    {
        if (rubric.EvaluationScope == "COMMON" && rubric.ProjectMode == "INTERDISCIPLINARY")
        {
            if (actor.DepartmentId != rubric.DepartmentId)
                throw new ForbiddenException("Only staff from the lead department can approve COMMON interdisciplinary rubrics.", "RUBRIC_SCOPE_NOT_LEAD_DEPARTMENT");
            return;
        }

        if (rubric.EvaluationScope == "MAJOR_SPECIFIC")
        {
            if (!rubric.MajorId.HasValue)
                throw new DomainException("A major-specific rubric must specify a major ID.", "RUBRIC_MAJOR_REQUIRED");
            var majorDeptId = await repository.GetMajorDepartmentIdAsync(rubric.MajorId.Value, ct);
            if (!majorDeptId.HasValue || actor.DepartmentId != majorDeptId.Value)
                throw new ForbiddenException("Only staff from the major's department can approve major-specific rubrics.", "RUBRIC_SCOPE_NOT_LEAD_DEPARTMENT");
            return;
        }

        if (rubric.EvaluationScope == "INDIVIDUAL")
        {
            if (rubric.MajorId.HasValue)
            {
                var majorDeptId = await repository.GetMajorDepartmentIdAsync(rubric.MajorId.Value, ct);
                if (!majorDeptId.HasValue || actor.DepartmentId != majorDeptId.Value)
                    throw new ForbiddenException("Only staff from the responsible department can approve individual rubrics.", "RUBRIC_SCOPE_NOT_LEAD_DEPARTMENT");
                return;
            }
            if (rubric.DepartmentId.HasValue)
            {
                if (actor.DepartmentId != rubric.DepartmentId.Value)
                    throw new ForbiddenException("Only staff from the responsible department can approve individual rubrics.", "RUBRIC_SCOPE_NOT_LEAD_DEPARTMENT");
                return;
            }
            throw new ForbiddenException("Unable to determine responsible department for individual rubric.", "RUBRIC_SCOPE_NOT_LEAD_DEPARTMENT");
        }

        if (rubric.ProjectMode == "SINGLE_MAJOR")
        {
            if (rubric.MajorId.HasValue)
            {
                var majorDeptId = await repository.GetMajorDepartmentIdAsync(rubric.MajorId.Value, ct);
                if (!majorDeptId.HasValue || majorDeptId.Value != rubric.DepartmentId)
                    throw new ForbiddenException("Rubric major does not belong to the rubric department.", "RUBRIC_SCOPE_NOT_LEAD_DEPARTMENT");
            }
            if (actor.DepartmentId != rubric.DepartmentId)
                throw new ForbiddenException("Only staff from the rubric department can approve single-major rubrics.", "RUBRIC_SCOPE_NOT_LEAD_DEPARTMENT");
            return;
        }

        if (actor.DepartmentId != rubric.DepartmentId)
            throw new ForbiddenException("The actor is outside the rubric's department scope.", "RUBRIC_SCOPE_NOT_LEAD_DEPARTMENT");
    }

    private async Task<(bool CanSubmit, bool CanApprove, bool CanReject)> ComputePermissionsAsync(
        RubricRecord rubric, RubricActor actor, CancellationToken ct)
    {
        bool canSubmit = false;
        bool canApprove = false;
        bool canReject = false;

        if (rubric.Status == RubricStatuses.Draft && !rubric.IsReferenced)
        {
            if (rubric.DepartmentId.HasValue && rubric.AcademicSemesterId.HasValue && !string.IsNullOrWhiteSpace(rubric.EvaluationScope))
            {
                if (actor.IsStaff && (actor.DepartmentId == rubric.DepartmentId || actor.IsAdmin))
                {
                    canSubmit = true;
                }
            }
        }
        else if (rubric.Status == RubricStatuses.PendingApproval)
        {
            if (actor.IsStaff && actor.DepartmentId.HasValue)
            {
                var approval = await repository.GetLatestApprovalAsync(rubric.Id, ct);
                if (approval is not null && !approval.DecidedAt.HasValue && actor.UserId != approval.SubmittedBy)
                {
                    bool scopeValid = false;
                    try
                    {
                        await ValidateApprovalScopeAuthority(actor, rubric, ct);
                        scopeValid = true;
                    }
                    catch (Exception)
                    {
                        scopeValid = false;
                    }

                    if (scopeValid)
                    {
                        canApprove = true;
                        canReject = true;
                    }
                }
            }
        }

        return (canSubmit, canApprove, canReject);
    }

    private async Task<RubricRecord> Load(long id, RubricActor actor, bool forUpdate, CancellationToken ct) =>
        await repository.GetAsync(id, actor, forUpdate, ct) ?? throw new NotFoundException("Rubric", id);

    private Task Audit(string action, RubricRecord rubric, long actor, CancellationToken ct, Dictionary<string, object?>? extra = null)
    {
        var data = new Dictionary<string, object?>
        {
            ["rootRubricId"] = rubric.RootRubricId,
            ["version"] = rubric.Version,
            ["status"] = rubric.Status
        };
        if (extra is not null)
        {
            foreach (var (k, v) in extra) data[k] = v;
        }
        return audit.RecordAsync(new AuditEntry(actor, action, "RUBRIC", rubric.Id, data), ct);
    }

    public async Task<PagedResult<RubricDto>> List(RubricFilter filter, CancellationToken ct)
    {
        var actor = await Actor(ct);
        if (!actor.IsAdmin && filter.DepartmentId.HasValue && filter.DepartmentId != actor.DepartmentId)
            throw new ForbiddenException();
        var result = await repository.ListAsync(actor, filter, ct);
        var dtos = new List<RubricDto>(result.Items.Count);
        foreach (var r in result.Items)
        {
            var perms = await ComputePermissionsAsync(r, actor, ct);
            dtos.Add(r.ToDto(perms.CanSubmit, perms.CanApprove, perms.CanReject));
        }
        return new(dtos.ToArray(), result.Page, result.PageSize, result.TotalCount);
    }

    public async Task<RubricDto> Get(long id, CancellationToken ct)
    {
        var actor = await Actor(ct);
        var rubric = await Load(id, actor, false, ct);
        var perms = await ComputePermissionsAsync(rubric, actor, ct);
        return rubric.ToDto(perms.CanSubmit, perms.CanApprove, perms.CanReject);
    }

    public Task<RubricDto> Create(CreateRubricRequest input, CancellationToken ct) => repository.InTransactionAsync(async () =>
    {
        var actor = await Actor(ct);
        await Scope(actor, input.DepartmentId, input.AcademicSemesterId, ct);
        RubricRules.EnsureValidHierarchy(input.Criteria);
        RubricRules.ValidateScope(input.EvaluationScope, input.MajorId, input.ProjectMode, input.EvaluationStage, input.EffectiveFrom, input.EffectiveTo);
        var rubric = await repository.CreateAsync(input.DepartmentId, input.AcademicSemesterId,
            input.Code.Trim().ToUpperInvariant(), input.Name.Trim(), input.Description?.Trim(), input.Criteria,
            actor.UserId, clock.GetUtcNow().UtcDateTime, null,
            input.MajorId, input.ProjectMode, input.EvaluationStage, input.EvaluationScope,
            input.EffectiveFrom, input.EffectiveTo, ct);
        await Audit("RUBRIC_CREATED", rubric, actor.UserId, ct);
        var perms = await ComputePermissionsAsync(rubric, actor, ct);
        return rubric.ToDto(perms.CanSubmit, perms.CanApprove, perms.CanReject);
    }, ct);

    public Task<RubricDto> Update(long id, UpdateRubricRequest input, CancellationToken ct) => repository.InTransactionAsync(async () =>
    {
        var actor = await Actor(ct);
        var rubric = await Load(id, actor, true, ct);
        RubricRules.EnsureCurrent(rubric, input.ConcurrencyToken);
        RubricRules.EnsureEditable(rubric);
        RubricRules.EnsureValidHierarchy(input.Criteria);
        await Scope(actor, rubric.DepartmentId!.Value, rubric.AcademicSemesterId!.Value, ct);
        RubricRules.ValidateScope(input.EvaluationScope, input.MajorId, input.ProjectMode, input.EvaluationStage, input.EffectiveFrom, input.EffectiveTo);
        var updated = await repository.UpdateAsync(id, input.Name.Trim(), input.Description?.Trim(),
            input.Criteria, clock.GetUtcNow().UtcDateTime,
            input.MajorId, input.ProjectMode, input.EvaluationStage, input.EvaluationScope,
            input.EffectiveFrom, input.EffectiveTo, ct);
        await Audit("RUBRIC_UPDATED", updated, actor.UserId, ct);
        var perms = await ComputePermissionsAsync(updated, actor, ct);
        return updated.ToDto(perms.CanSubmit, perms.CanApprove, perms.CanReject);
    }, ct);

    public Task<RubricDto> SubmitForApproval(long id, SubmitRubricForApprovalRequest input, CancellationToken ct) => repository.InTransactionAsync(async () =>
    {
        var actor = await Actor(ct);
        var rubric = await Load(id, actor, true, ct);
        RubricRules.EnsureCurrent(rubric, input.ConcurrencyToken);
        if (rubric.Status != RubricStatuses.Draft)
            throw new ConflictException("Only draft rubrics can be submitted for approval.");
        if (rubric.DepartmentId is null || rubric.AcademicSemesterId is null || string.IsNullOrWhiteSpace(rubric.EvaluationScope))
            throw new ConflictException("Legacy unscoped rubrics cannot be submitted for approval.", "RUBRIC_SCOPE_REQUIRED");
        var existingApproval = await repository.GetLatestApprovalAsync(id, ct);
        if (existingApproval is not null && !existingApproval.DecidedAt.HasValue)
            throw new ConflictException("An approval request is already pending for this rubric.");
        await Scope(actor, rubric.DepartmentId.Value, rubric.AcademicSemesterId.Value, ct);
        RubricRules.EnsureValidHierarchy(RubricHierarchy.ToInputs(rubric.Criteria));
        RubricRules.EnsurePublishable(rubric.Criteria, options.RequiredLevelCount, requireLevels: true);
        await repository.SubmitForApprovalAsync(id, actor.UserId, clock.GetUtcNow().UtcDateTime, ct);
        var updated = await Load(id, actor, false, ct);
        await Audit("RUBRIC_SUBMITTED_FOR_APPROVAL", updated, actor.UserId, ct, new() { ["submittedBy"] = actor.UserId });
        var perms = await ComputePermissionsAsync(updated, actor, ct);
        return updated.ToDto(perms.CanSubmit, perms.CanApprove, perms.CanReject);
    }, ct);

    public Task<RubricDto> Approve(long id, ApproveRubricRequest input, CancellationToken ct) => repository.InTransactionAsync(async () =>
    {
        var actor = await Actor(ct);
        var rubric = await Load(id, actor, true, ct);
        RubricRules.EnsureCurrent(rubric, input.ConcurrencyToken);
        if (rubric.Status != RubricStatuses.PendingApproval)
            throw new ConflictException("Only rubrics pending approval can be approved.");
        if (rubric.DepartmentId is null || rubric.AcademicSemesterId is null)
            throw new ConflictException("Legacy unscoped rubrics cannot be approved.");
        var approval = await repository.GetLatestApprovalAsync(id, ct);
        if (approval is null || approval.DecidedAt.HasValue)
            throw new ConflictException("No active approval request found for this rubric.");
        if (actor.UserId == approval.SubmittedBy)
            throw new ForbiddenException("Submitter cannot approve their own rubric.", "RUBRIC_SELF_APPROVAL_FORBIDDEN");
        if (!actor.IsStaff)
        {
            if (actor.IsAdmin)
                throw new ForbiddenException("Administrators without an academic department scope cannot approve rubrics.", "RUBRIC_APPROVAL_ADMIN_NOT_ACADEMIC");
            throw new ForbiddenException("Only academic department staff can approve rubrics.");
        }
        if (!actor.DepartmentId.HasValue)
            throw new ForbiddenException("Administrators without an academic department scope cannot approve rubrics.", "RUBRIC_APPROVAL_ADMIN_NOT_ACADEMIC");
        var scope = await repository.GetScopeAsync(rubric.DepartmentId.Value, rubric.AcademicSemesterId.Value, ct)
            ?? throw new ConflictException("Rubric department and semester must belong to the same active organization and department.");
        if (scope.SemesterStatus is "CLOSED" or "ARCHIVED")
            throw new ConflictException("Rubrics cannot be changed in a closed or archived semester.");
        await ValidateApprovalScopeAuthority(actor, rubric, ct);
        await repository.ApproveAsync(id, approval.Id, actor.UserId, input.ApprovalReference, clock.GetUtcNow().UtcDateTime, ct);
        var updated = await Load(id, actor, false, ct);
        await Audit("RUBRIC_APPROVED", updated, actor.UserId, ct, new()
        {
            ["approvalId"] = approval.Id,
            ["decision"] = "APPROVED",
            ["approvalReference"] = input.ApprovalReference
        });
        var perms = await ComputePermissionsAsync(updated, actor, ct);
        return updated.ToDto(perms.CanSubmit, perms.CanApprove, perms.CanReject);
    }, ct);

    public Task<RubricDto> Reject(long id, RejectRubricRequest input, CancellationToken ct) => repository.InTransactionAsync(async () =>
    {
        var actor = await Actor(ct);
        if (string.IsNullOrWhiteSpace(input.Reason))
            throw new DomainException("Rejection reason is required.", "RUBRIC_REJECTION_REASON_REQUIRED");
        var rubric = await Load(id, actor, true, ct);
        RubricRules.EnsureCurrent(rubric, input.ConcurrencyToken);
        if (rubric.Status != RubricStatuses.PendingApproval)
            throw new ConflictException("Only rubrics pending approval can be rejected.");
        if (rubric.DepartmentId is null || rubric.AcademicSemesterId is null)
            throw new ConflictException("Legacy unscoped rubrics cannot be rejected.");
        var approval = await repository.GetLatestApprovalAsync(id, ct);
        if (approval is null || approval.DecidedAt.HasValue)
            throw new ConflictException("No active approval request found for this rubric.");
        if (actor.UserId == approval.SubmittedBy)
            throw new ForbiddenException("Submitter cannot reject their own rubric.", "RUBRIC_SELF_APPROVAL_FORBIDDEN");
        if (!actor.IsStaff)
        {
            if (actor.IsAdmin)
                throw new ForbiddenException("Administrators without an academic department scope cannot reject rubrics.", "RUBRIC_APPROVAL_ADMIN_NOT_ACADEMIC");
            throw new ForbiddenException("Only academic department staff can reject rubrics.");
        }
        if (!actor.DepartmentId.HasValue)
            throw new ForbiddenException("Administrators without an academic department scope cannot reject rubrics.", "RUBRIC_APPROVAL_ADMIN_NOT_ACADEMIC");
        var scope = await repository.GetScopeAsync(rubric.DepartmentId.Value, rubric.AcademicSemesterId.Value, ct)
            ?? throw new ConflictException("Rubric department and semester must belong to the same active organization and department.");
        if (scope.SemesterStatus is "CLOSED" or "ARCHIVED")
            throw new ConflictException("Rubrics cannot be changed in a closed or archived semester.");
        await ValidateApprovalScopeAuthority(actor, rubric, ct);
        await repository.RejectAsync(id, approval.Id, actor.UserId, input.Reason.Trim(), clock.GetUtcNow().UtcDateTime, ct);
        var updated = await Load(id, actor, false, ct);
        await Audit("RUBRIC_REJECTED", updated, actor.UserId, ct, new()
        {
            ["approvalId"] = approval.Id,
            ["decision"] = "REJECTED",
            ["reason"] = input.Reason.Trim()
        });
        var perms = await ComputePermissionsAsync(updated, actor, ct);
        return updated.ToDto(perms.CanSubmit, perms.CanApprove, perms.CanReject);
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
            if (rubric.EvaluationScope is not null || rubric.ProjectMode is not null || rubric.EvaluationStage is not null || rubric.MajorId is not null)
                throw new ConflictException("Direct publish is not allowed for scoped rubrics. Use the approval workflow instead.", "RUBRIC_REQUIRES_APPROVAL_FLOW");
            RubricRules.EnsureEditable(rubric);
            RubricRules.EnsurePublishable(rubric.Criteria, options.RequiredLevelCount);
        }
        else if (rubric.Status != RubricStatuses.Published)
            throw new ConflictException("Only published rubrics can be retired.");
        var updated = await repository.SetStatusAsync(id, publish ? RubricStatuses.Published : RubricStatuses.Retired,
            clock.GetUtcNow().UtcDateTime, ct);
        await Audit(publish ? "RUBRIC_PUBLISHED" : "RUBRIC_RETIRED", updated, actor.UserId, ct);
        var perms = await ComputePermissionsAsync(updated, actor, ct);
        return updated.ToDto(perms.CanSubmit, perms.CanApprove, perms.CanReject);
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
            RubricHierarchy.ToInputs(rubric.Criteria), actor.UserId, clock.GetUtcNow().UtcDateTime, id,
            rubric.MajorId, rubric.ProjectMode, rubric.EvaluationStage, rubric.EvaluationScope,
            rubric.EffectiveFrom, rubric.EffectiveTo, ct);
        await Audit("RUBRIC_VERSION_CREATED", created, actor.UserId, ct);
        var perms = await ComputePermissionsAsync(created, actor, ct);
        return created.ToDto(perms.CanSubmit, perms.CanApprove, perms.CanReject);
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
