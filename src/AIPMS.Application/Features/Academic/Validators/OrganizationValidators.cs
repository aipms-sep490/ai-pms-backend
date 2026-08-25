using AIPMS.Application.Features.Academic.Commands;
using AIPMS.Application.Features.Academic.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.Academic.Validators;

public sealed class GetOrganizationsQueryValidator : AbstractValidator<GetOrganizationsQuery>
{
    public GetOrganizationsQueryValidator()
    {
        this.AddPagingRules(
            static query => query.Page,
            static query => query.PageSize,
            static query => query.Search);
    }
}

public sealed class GetOrganizationByIdQueryValidator
    : AbstractValidator<GetOrganizationByIdQuery>
{
    public GetOrganizationByIdQueryValidator() =>
        RuleFor(static query => query.OrganizationId).GreaterThan(0);
}

public sealed class CreateOrganizationCommandValidator
    : AbstractValidator<CreateOrganizationCommand>
{
    public CreateOrganizationCommandValidator()
    {
        RuleFor(static command => command.Code).ValidAcademicCode();
        RuleFor(static command => command.Name).ValidAcademicName();
        this.AddDescriptionRule(static command => command.Description);
    }
}

public sealed class UpdateOrganizationCommandValidator
    : AbstractValidator<UpdateOrganizationCommand>
{
    public UpdateOrganizationCommandValidator()
    {
        RuleFor(static command => command.OrganizationId).GreaterThan(0);
        RuleFor(static command => command.Code).ValidAcademicCode();
        RuleFor(static command => command.Name).ValidAcademicName();
        this.AddDescriptionRule(static command => command.Description);
    }
}

public sealed class SetOrganizationStatusCommandValidator
    : AbstractValidator<SetOrganizationStatusCommand>
{
    public SetOrganizationStatusCommandValidator() =>
        RuleFor(static command => command.OrganizationId).GreaterThan(0);
}
