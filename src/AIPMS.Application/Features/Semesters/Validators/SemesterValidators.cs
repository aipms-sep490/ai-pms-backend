using AIPMS.Application.Features.Semesters.Commands;
using AIPMS.Application.Features.Semesters.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.Semesters.Validators;

// ── Semester validators ───────────────────────────────────────────────────────

public sealed class GetSemestersQueryValidator : AbstractValidator<GetSemestersQuery>
{
    public GetSemestersQueryValidator()
    {
        RuleFor(static q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(static q => q.PageSize).InclusiveBetween(1, 100);
        RuleFor(static q => q.Search).MaximumLength(255);
        RuleFor(static q => q.Status)
            .Must(static s => s is null || SemesterStatuses.All.Contains(s))
            .WithMessage($"Status must be one of: {string.Join(", ", SemesterStatuses.All)}.");
    }
}

public sealed class GetSemesterByIdQueryValidator : AbstractValidator<GetSemesterByIdQuery>
{
    public GetSemesterByIdQueryValidator() =>
        RuleFor(static q => q.SemesterId).GreaterThan(0);
}

public sealed class CreateSemesterCommandValidator : AbstractValidator<CreateSemesterCommand>
{
    public CreateSemesterCommandValidator()
    {
        RuleFor(static c => c.OrganizationId).GreaterThan(0);
        RuleFor(static c => c.Code).NotEmpty().MaximumLength(50)
            .Matches("^[A-Za-z0-9][A-Za-z0-9_-]*$")
            .WithMessage("Code may contain only letters, numbers, underscores and hyphens.");
        RuleFor(static c => c.Name).NotEmpty().MaximumLength(255);
        RuleFor(static c => c.EndDate)
            .GreaterThanOrEqualTo(static c => c.StartDate)
            .WithMessage("EndDate must be on or after StartDate.");
    }
}

public sealed class UpdateSemesterCommandValidator : AbstractValidator<UpdateSemesterCommand>
{
    public UpdateSemesterCommandValidator()
    {
        RuleFor(static c => c.SemesterId).GreaterThan(0);
        RuleFor(static c => c.Code).NotEmpty().MaximumLength(50)
            .Matches("^[A-Za-z0-9][A-Za-z0-9_-]*$")
            .WithMessage("Code may contain only letters, numbers, underscores and hyphens.");
        RuleFor(static c => c.Name).NotEmpty().MaximumLength(255);
        RuleFor(static c => c.EndDate)
            .GreaterThanOrEqualTo(static c => c.StartDate)
            .WithMessage("EndDate must be on or after StartDate.");
    }
}

public sealed class SetSemesterStatusCommandValidator : AbstractValidator<SetSemesterStatusCommand>
{
    public SetSemesterStatusCommandValidator()
    {
        RuleFor(static c => c.SemesterId).GreaterThan(0);
        RuleFor(static c => c.Status)
            .NotEmpty()
            .Must(static s => SemesterStatuses.All.Contains(s))
            .WithMessage($"Status must be one of: {string.Join(", ", SemesterStatuses.All)}.");
    }
}

// ── ProjectPeriod validators ──────────────────────────────────────────────────

public sealed class GetProjectPeriodsQueryValidator : AbstractValidator<GetProjectPeriodsQuery>
{
    public GetProjectPeriodsQueryValidator()
    {
        RuleFor(static q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(static q => q.PageSize).InclusiveBetween(1, 100);
        RuleFor(static q => q.Search).MaximumLength(255);
        RuleFor(static q => q.Status)
            .Must(static s => s is null || SemesterStatuses.All.Contains(s))
            .WithMessage($"Status must be one of: {string.Join(", ", SemesterStatuses.All)}.");
        RuleFor(static q => q.PeriodType)
            .Must(static t => t is null || PeriodTypes.All.Contains(t))
            .WithMessage($"PeriodType must be one of: {string.Join(", ", PeriodTypes.All)}.");
    }
}

public sealed class GetProjectPeriodByIdQueryValidator : AbstractValidator<GetProjectPeriodByIdQuery>
{
    public GetProjectPeriodByIdQueryValidator() =>
        RuleFor(static q => q.PeriodId).GreaterThan(0);
}

public sealed class CreateProjectPeriodCommandValidator : AbstractValidator<CreateProjectPeriodCommand>
{
    public CreateProjectPeriodCommandValidator()
    {
        RuleFor(static c => c.AcademicSemesterId).GreaterThan(0);
        RuleFor(static c => c.Code).NotEmpty().MaximumLength(50)
            .Matches("^[A-Za-z0-9][A-Za-z0-9_-]*$")
            .WithMessage("Code may contain only letters, numbers, underscores and hyphens.");
        RuleFor(static c => c.Name).NotEmpty().MaximumLength(255);
        RuleFor(static c => c.PeriodType)
            .NotEmpty()
            .Must(static t => PeriodTypes.All.Contains(t))
            .WithMessage($"PeriodType must be one of: {string.Join(", ", PeriodTypes.All)}.");
        RuleFor(static c => c.EndAt)
            .GreaterThanOrEqualTo(static c => c.StartAt)
            .WithMessage("EndAt must be on or after StartAt.");
    }
}

public sealed class UpdateProjectPeriodCommandValidator : AbstractValidator<UpdateProjectPeriodCommand>
{
    public UpdateProjectPeriodCommandValidator()
    {
        RuleFor(static c => c.PeriodId).GreaterThan(0);
        RuleFor(static c => c.Code).NotEmpty().MaximumLength(50)
            .Matches("^[A-Za-z0-9][A-Za-z0-9_-]*$")
            .WithMessage("Code may contain only letters, numbers, underscores and hyphens.");
        RuleFor(static c => c.Name).NotEmpty().MaximumLength(255);
        RuleFor(static c => c.PeriodType)
            .NotEmpty()
            .Must(static t => PeriodTypes.All.Contains(t))
            .WithMessage($"PeriodType must be one of: {string.Join(", ", PeriodTypes.All)}.");
        RuleFor(static c => c.EndAt)
            .GreaterThanOrEqualTo(static c => c.StartAt)
            .WithMessage("EndAt must be on or after StartAt.");
    }
}

public sealed class SetProjectPeriodStatusCommandValidator : AbstractValidator<SetProjectPeriodStatusCommand>
{
    public SetProjectPeriodStatusCommandValidator()
    {
        RuleFor(static c => c.PeriodId).GreaterThan(0);
        RuleFor(static c => c.Status)
            .NotEmpty()
            .Must(static s => SemesterStatuses.All.Contains(s))
            .WithMessage($"Status must be one of: {string.Join(", ", SemesterStatuses.All)}.");
    }
}
