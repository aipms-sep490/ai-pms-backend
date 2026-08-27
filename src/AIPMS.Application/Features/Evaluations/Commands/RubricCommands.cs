using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Evaluations.DTOs;
using FluentValidation;
using MediatR;

namespace AIPMS.Application.Features.Evaluations.Commands;

public sealed record CreateRubricCommand(long? DepartmentId, long? AcademicSemesterId, string Code,
    string Name, string? Description, int VersionNumber) : IRequest<RubricDto>;
public sealed record UpdateRubricCommand(long Id, string Name, string? Description) : IRequest<Unit>;
public sealed record DeleteRubricCommand(long Id) : IRequest<Unit>;
public sealed record UpsertRubricCriterionCommand(long RubricId, long CriterionId,
    decimal WeightPercent, decimal MaxScore, int SortOrder, bool IsRequired) : IRequest<Unit>;
public sealed record DeleteRubricCriterionCommand(long RubricId, long RubricCriterionId) : IRequest<Unit>;
public sealed record ReorderRubricCriteriaCommand(long RubricId, IReadOnlyList<long> OrderedIds) : IRequest<Unit>;
public sealed record SetRubricActiveCommand(long Id, bool Active) : IRequest<Unit>;

public sealed class CreateRubricValidator : AbstractValidator<CreateRubricCommand>
{ public CreateRubricValidator() { RuleFor(x => x).Must(x => x.DepartmentId.HasValue || x.AcademicSemesterId.HasValue).WithMessage("A rubric requires department or semester scope."); RuleFor(x => x.Code).NotEmpty().MaximumLength(50); RuleFor(x => x.Name).NotEmpty().MaximumLength(255); RuleFor(x => x.VersionNumber).GreaterThan(0); } }
public sealed class UpdateRubricValidator : AbstractValidator<UpdateRubricCommand>
{ public UpdateRubricValidator() { RuleFor(x => x.Id).GreaterThan(0); RuleFor(x => x.Name).NotEmpty().MaximumLength(255); } }
public sealed class DeleteRubricValidator : AbstractValidator<DeleteRubricCommand>
{ public DeleteRubricValidator() { RuleFor(x => x.Id).GreaterThan(0); } }
public sealed class UpsertRubricCriterionValidator : AbstractValidator<UpsertRubricCriterionCommand>
{ public UpsertRubricCriterionValidator() { RuleFor(x => x.RubricId).GreaterThan(0); RuleFor(x => x.CriterionId).GreaterThan(0); RuleFor(x => x.WeightPercent).InclusiveBetween(0.01m, 100m); RuleFor(x => x.MaxScore).GreaterThan(0); RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0); } }
public sealed class DeleteRubricCriterionValidator : AbstractValidator<DeleteRubricCriterionCommand>
{ public DeleteRubricCriterionValidator() { RuleFor(x => x.RubricId).GreaterThan(0); RuleFor(x => x.RubricCriterionId).GreaterThan(0); } }
public sealed class ReorderRubricCriteriaValidator : AbstractValidator<ReorderRubricCriteriaCommand>
{ public ReorderRubricCriteriaValidator() { RuleFor(x => x.RubricId).GreaterThan(0); RuleFor(x => x.OrderedIds).NotEmpty().Must(x => x.Distinct().Count() == x.Count).WithMessage("Criterion IDs must be unique."); RuleForEach(x => x.OrderedIds).GreaterThan(0); } }
public sealed class SetRubricActiveValidator : AbstractValidator<SetRubricActiveCommand>
{ public SetRubricActiveValidator() { RuleFor(x => x.Id).GreaterThan(0); } }

internal static class RubricAccess
{
    internal static long EnsureStaff(ICurrentUser user)
    {
        if (!user.Roles.Contains(AppRoles.Admin) && !user.Roles.Contains(AppRoles.DepartmentStaff))
            throw new ForbiddenException("Only ADMIN or DEPARTMENT_STAFF can manage rubrics.");
        return user.UserId ?? throw new ForbiddenException();
    }
    internal static void EnsureDraft(RubricDto rubric)
    { if (rubric.ApprovalStatus != "DRAFT") throw new ConflictException("Approved rubric versions are immutable; create a new version instead."); }
}

public sealed class CreateRubricHandler(ICurrentUser user, IEvaluationRepository repository) : IRequestHandler<CreateRubricCommand, RubricDto>
{ public async Task<RubricDto> Handle(CreateRubricCommand r, CancellationToken ct) { var actor = RubricAccess.EnsureStaff(user); var id = await repository.CreateRubricAsync(new(r.DepartmentId, r.AcademicSemesterId, r.Code.Trim().ToUpperInvariant(), r.Name.Trim(), r.Description?.Trim(), r.VersionNumber, actor), ct); return await repository.GetRubricAsync(id, ct) ?? throw new NotFoundException("Rubric", id); } }
public sealed class UpdateRubricHandler(ICurrentUser user, IEvaluationRepository repository) : IRequestHandler<UpdateRubricCommand, Unit>
{ public async Task<Unit> Handle(UpdateRubricCommand r, CancellationToken ct) { RubricAccess.EnsureStaff(user); var item = await repository.GetRubricAsync(r.Id, ct) ?? throw new NotFoundException("Rubric", r.Id); RubricAccess.EnsureDraft(item); if (!await repository.UpdateRubricAsync(r.Id, new(r.Name.Trim(), r.Description?.Trim()), ct)) throw new ConflictException("Rubric was changed concurrently."); return Unit.Value; } }
public sealed class DeleteRubricHandler(ICurrentUser user, IEvaluationRepository repository) : IRequestHandler<DeleteRubricCommand, Unit>
{ public async Task<Unit> Handle(DeleteRubricCommand r, CancellationToken ct) { RubricAccess.EnsureStaff(user); var item = await repository.GetRubricAsync(r.Id, ct) ?? throw new NotFoundException("Rubric", r.Id); RubricAccess.EnsureDraft(item); if (!await repository.DeleteRubricAsync(r.Id, ct)) throw new ConflictException("Rubric is referenced or was changed concurrently."); return Unit.Value; } }
public sealed class UpsertRubricCriterionHandler(ICurrentUser user, IEvaluationRepository repository) : IRequestHandler<UpsertRubricCriterionCommand, Unit>
{ public async Task<Unit> Handle(UpsertRubricCriterionCommand r, CancellationToken ct) { RubricAccess.EnsureStaff(user); var item = await repository.GetRubricAsync(r.RubricId, ct) ?? throw new NotFoundException("Rubric", r.RubricId); RubricAccess.EnsureDraft(item); if (!await repository.UpsertRubricCriterionAsync(r.RubricId, new(r.CriterionId, r.WeightPercent, r.MaxScore, r.SortOrder, r.IsRequired), ct)) throw new NotFoundException("EvaluationCriterion", r.CriterionId); return Unit.Value; } }
public sealed class DeleteRubricCriterionHandler(ICurrentUser user, IEvaluationRepository repository) : IRequestHandler<DeleteRubricCriterionCommand, Unit>
{ public async Task<Unit> Handle(DeleteRubricCriterionCommand r, CancellationToken ct) { RubricAccess.EnsureStaff(user); var item = await repository.GetRubricAsync(r.RubricId, ct) ?? throw new NotFoundException("Rubric", r.RubricId); RubricAccess.EnsureDraft(item); if (!await repository.DeleteRubricCriterionAsync(r.RubricId, r.RubricCriterionId, ct)) throw new NotFoundException("RubricCriterion", r.RubricCriterionId); return Unit.Value; } }
public sealed class ReorderRubricCriteriaHandler(ICurrentUser user, IEvaluationRepository repository) : IRequestHandler<ReorderRubricCriteriaCommand, Unit>
{ public async Task<Unit> Handle(ReorderRubricCriteriaCommand r, CancellationToken ct) { RubricAccess.EnsureStaff(user); var item = await repository.GetRubricAsync(r.RubricId, ct) ?? throw new NotFoundException("Rubric", r.RubricId); RubricAccess.EnsureDraft(item); if (!await repository.ReorderRubricCriteriaAsync(r.RubricId, r.OrderedIds, ct)) throw new ConflictException("The ordered criteria must exactly match the rubric criteria."); return Unit.Value; } }
public sealed class SetRubricActiveHandler(ICurrentUser user, IEvaluationRepository repository, TimeProvider clock) : IRequestHandler<SetRubricActiveCommand, Unit>
{ public async Task<Unit> Handle(SetRubricActiveCommand r, CancellationToken ct) { var actor = RubricAccess.EnsureStaff(user); var item = await repository.GetRubricAsync(r.Id, ct) ?? throw new NotFoundException("Rubric", r.Id); if (r.Active) { RubricAccess.EnsureDraft(item); if (item.Criteria.Count == 0 || item.Criteria.Sum(x => x.WeightPercent) != 100m) throw new ConflictException("Rubric criteria weights must total exactly 100 before activation."); } if (!await repository.SetRubricActiveAsync(r.Id, r.Active, actor, clock.GetUtcNow().UtcDateTime, ct)) throw new ConflictException("Rubric state was changed concurrently."); return Unit.Value; } }

