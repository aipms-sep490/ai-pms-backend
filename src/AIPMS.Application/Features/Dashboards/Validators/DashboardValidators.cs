using AIPMS.Application.Features.Dashboards.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.Dashboards.Validators;

public sealed class GetStudentDashboardQueryValidator : AbstractValidator<GetStudentDashboardQuery>
{
    public GetStudentDashboardQueryValidator() => RuleFor(q => q.SemesterId).GreaterThan(0);
}

public sealed class GetSupervisorDashboardQueryValidator : AbstractValidator<GetSupervisorDashboardQuery>
{
    public GetSupervisorDashboardQueryValidator()
    {
        RuleFor(q => q.SemesterId).GreaterThan(0);
        RuleFor(q => q.Page).InclusiveBetween(1, 1000000);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 50);
        RuleFor(q => q.Search).MaximumLength(200);
        RuleFor(q => q.Status).Must(s => s is null or "DRAFT" or "SUBMITTED" or "UNDER_REVIEW"
            or "REVISION_REQUIRED" or "REJECTED" or "APPROVED" or "SUPERVISOR_PENDING" or "ACTIVE"
            or "FINAL_SUBMISSION" or "COMPLETED" or "ARCHIVED");
    }
}

public sealed class GetDepartmentDashboardQueryValidator : AbstractValidator<GetDepartmentDashboardQuery>
{
    public GetDepartmentDashboardQueryValidator()
    {
        RuleFor(q => q.SemesterId).GreaterThan(0);
        RuleFor(q => q.MajorId).GreaterThan(0);
        RuleFor(q => q.Page).InclusiveBetween(1, 1000000);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
        RuleFor(q => q.Search).MaximumLength(200);
        RuleFor(q => q.Status).Must(s => s is null or "DRAFT" or "SUBMITTED" or "UNDER_REVIEW"
            or "REVISION_REQUIRED" or "REJECTED" or "APPROVED" or "SUPERVISOR_PENDING" or "ACTIVE"
            or "FINAL_SUBMISSION" or "COMPLETED" or "ARCHIVED");
    }
}

public sealed class GetAdminDashboardQueryValidator : AbstractValidator<GetAdminDashboardQuery>
{
    public GetAdminDashboardQueryValidator()
    {
        RuleFor(q => q.SemesterId).GreaterThan(0);
        RuleFor(q => q.DepartmentId).GreaterThan(0);
        RuleFor(q => q.MajorId).GreaterThan(0);
        RuleFor(q => q.Page).InclusiveBetween(1, 1000000);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
        RuleFor(q => q.Search).MaximumLength(200);
        RuleFor(q => q.Status).Must(s => s is null or "DRAFT" or "SUBMITTED" or "UNDER_REVIEW"
            or "REVISION_REQUIRED" or "REJECTED" or "APPROVED" or "SUPERVISOR_PENDING" or "ACTIVE"
            or "FINAL_SUBMISSION" or "COMPLETED" or "ARCHIVED");
    }
}

public sealed class ExportPortfolioDashboardQueryValidator : AbstractValidator<ExportPortfolioDashboardQuery>
{
    public ExportPortfolioDashboardQueryValidator()
    {
        RuleFor(q => q.SemesterId).GreaterThan(0);
        RuleFor(q => q.DepartmentId).GreaterThan(0);
        RuleFor(q => q.MajorId).GreaterThan(0);
        RuleFor(q => q.Search).MaximumLength(200);
        RuleFor(q => q.Format).Must(f => f is null or "csv")
            .WithMessage("Only CSV export is supported.");
        RuleFor(q => q.Status).Must(s => s is null or "DRAFT" or "SUBMITTED" or "UNDER_REVIEW"
            or "REVISION_REQUIRED" or "REJECTED" or "APPROVED" or "SUPERVISOR_PENDING" or "ACTIVE"
            or "FINAL_SUBMISSION" or "COMPLETED" or "ARCHIVED");
    }
}
