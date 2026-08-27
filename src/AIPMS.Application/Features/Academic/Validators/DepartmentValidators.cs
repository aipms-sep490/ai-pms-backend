using AIPMS.Application.Features.Academic.Commands;
using AIPMS.Application.Features.Academic.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.Academic.Validators;

public sealed class GetDepartmentsQueryValidator : AbstractValidator<GetDepartmentsQuery>
{
    public GetDepartmentsQueryValidator()
    {
        RuleFor(static query => query.OrganizationId)
            .GreaterThan(0)
            .When(static query => query.OrganizationId.HasValue);
        this.AddPagingRules(
            static query => query.Page,
            static query => query.PageSize,
            static query => query.Search);
    }
}

public sealed class GetDepartmentByIdQueryValidator
    : AbstractValidator<GetDepartmentByIdQuery>
{
    public GetDepartmentByIdQueryValidator() =>
        RuleFor(static query => query.DepartmentId).GreaterThan(0);
}

public sealed class CreateDepartmentCommandValidator
    : AbstractValidator<CreateDepartmentCommand>
{
    public CreateDepartmentCommandValidator()
    {
        RuleFor(static command => command.OrganizationId).GreaterThan(0);
        RuleFor(static command => command.Code).ValidAcademicCode();
        RuleFor(static command => command.Name).ValidAcademicName();
        this.AddDescriptionRule(static command => command.Description);
    }
}

public sealed class UpdateDepartmentCommandValidator
    : AbstractValidator<UpdateDepartmentCommand>
{
    public UpdateDepartmentCommandValidator()
    {
        RuleFor(static command => command.DepartmentId).GreaterThan(0);
        RuleFor(static command => command.Code).ValidAcademicCode();
        RuleFor(static command => command.Name).ValidAcademicName();
        this.AddDescriptionRule(static command => command.Description);
    }
}

public sealed class SetDepartmentStatusCommandValidator
    : AbstractValidator<SetDepartmentStatusCommand>
{
    public SetDepartmentStatusCommandValidator() =>
        RuleFor(static command => command.DepartmentId).GreaterThan(0);
}
