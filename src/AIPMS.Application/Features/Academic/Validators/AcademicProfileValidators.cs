using AIPMS.Application.Features.Academic.Commands;
using AIPMS.Application.Features.Academic.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.Academic.Validators;

public sealed class VerifyAcademicProfileValidator : AbstractValidator<VerifyAcademicProfileCommand>
{
    public VerifyAcademicProfileValidator() => RuleFor(r => r.UserId).GreaterThan(0);
}
public sealed class RejectAcademicProfileValidator : AbstractValidator<RejectAcademicProfileCommand>
{
    public RejectAcademicProfileValidator()
    {
        RuleFor(r => r.UserId).GreaterThan(0);
        RuleFor(r => r.Reason).NotEmpty().MaximumLength(2000);
    }
}
public sealed class GetAcademicProfilesValidator : AbstractValidator<GetAcademicProfilesQuery>
{
    public GetAcademicProfilesValidator()
    {
        RuleFor(r => r.Page).InclusiveBetween(1, 1000000);
        RuleFor(r => r.PageSize).InclusiveBetween(1, 100);
        RuleFor(r => r.DepartmentId).GreaterThan(0).When(r => r.DepartmentId.HasValue);
        RuleFor(r => r.Status).Must(s => s is null or "PENDING" or "VERIFIED" or "REJECTED");
    }
}
