using AIPMS.Application.Features.Academic.Commands;
using AIPMS.Application.Features.Academic.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.Academic.Validators;

public sealed class GetMajorsQueryValidator : AbstractValidator<GetMajorsQuery>
{
    public GetMajorsQueryValidator()
    {
        RuleFor(static query => query.OrganizationId)
            .GreaterThan(0)
            .When(static query => query.OrganizationId.HasValue);
        RuleFor(static query => query.DepartmentId)
            .GreaterThan(0)
            .When(static query => query.DepartmentId.HasValue);
        this.AddPagingRules(
            static query => query.Page,
            static query => query.PageSize,
            static query => query.Search);
    }
}

public sealed class GetMajorByIdQueryValidator : AbstractValidator<GetMajorByIdQuery>
{
    public GetMajorByIdQueryValidator() =>
        RuleFor(static query => query.MajorId).GreaterThan(0);
}

public sealed class GetAcademicHierarchyQueryValidator
    : AbstractValidator<GetAcademicHierarchyQuery>
{
    public GetAcademicHierarchyQueryValidator()
    {
        RuleFor(static query => query.OrganizationId)
            .GreaterThan(0)
            .When(static query => query.OrganizationId.HasValue);
        RuleFor(static query => query.Search).MaximumLength(255);
    }
}

public sealed class CreateMajorCommandValidator : AbstractValidator<CreateMajorCommand>
{
    public CreateMajorCommandValidator()
    {
        RuleFor(static command => command.DepartmentId).GreaterThan(0);
        RuleFor(static command => command.Code).ValidAcademicCode();
        RuleFor(static command => command.Name).ValidAcademicName();
        this.AddDescriptionRule(static command => command.Description);
    }
}

public sealed class UpdateMajorCommandValidator : AbstractValidator<UpdateMajorCommand>
{
    public UpdateMajorCommandValidator()
    {
        RuleFor(static command => command.MajorId).GreaterThan(0);
        RuleFor(static command => command.DepartmentId).GreaterThan(0);
        RuleFor(static command => command.Code).ValidAcademicCode();
        RuleFor(static command => command.Name).ValidAcademicName();
        this.AddDescriptionRule(static command => command.Description);
    }
}

public sealed class SetMajorStatusCommandValidator
    : AbstractValidator<SetMajorStatusCommand>
{
    public SetMajorStatusCommandValidator() =>
        RuleFor(static command => command.MajorId).GreaterThan(0);
}
