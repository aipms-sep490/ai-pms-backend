using System;
using System.Linq;
using FluentValidation;
using AIPMS.Application.Features.Projects.Commands;
using AIPMS.Application.Features.Projects.Queries;

namespace AIPMS.Application.Features.Projects.Validators;

public sealed class CreateProjectDraftCommandValidator : AbstractValidator<CreateProjectDraftCommand>
{
    public CreateProjectDraftCommandValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(500).WithMessage("Title must not exceed 500 characters.");

        RuleFor(x => x.Domain)
            .NotEmpty().WithMessage("Domain is required.")
            .MaximumLength(100).WithMessage("Domain must not exceed 100 characters.");

        RuleFor(x => x.RequiredMajorIds)
            .NotNull().WithMessage("RequiredMajorIds cannot be null.")
            .Must(x => x.Distinct().Count() == x.Count).WithMessage("RequiredMajorIds must not contain duplicate values.");

        RuleForEach(x => x.Technologies)
            .NotEmpty().WithMessage("Technology tag must not be empty.")
            .MaximumLength(100).WithMessage("Technology tag must not exceed 100 characters.");

        RuleFor(x => x.Technologies)
            .Must(x => x.Select(t => t.Trim().ToLowerInvariant()).Distinct().Count() == x.Count)
            .WithMessage("Technologies must not contain duplicate tags.");

        RuleForEach(x => x.Keywords)
            .NotEmpty().WithMessage("Keyword tag must not be empty.")
            .MaximumLength(100).WithMessage("Keyword tag must not exceed 100 characters.");

        RuleFor(x => x.Keywords)
            .Must(x => x.Select(k => k.Trim().ToLowerInvariant()).Distinct().Count() == x.Count)
            .WithMessage("Keywords must not contain duplicate tags.");
    }
}

public sealed class UpdateProjectDraftCommandValidator : AbstractValidator<UpdateProjectDraftCommand>
{
    public UpdateProjectDraftCommandValidator()
    {
        RuleFor(x => x.ProjectId)
            .GreaterThan(0).WithMessage("ProjectId must be greater than 0.");

        RuleFor(x => x.ConcurrencyToken)
            .NotEmpty().WithMessage("ConcurrencyToken is required.");

        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(500).WithMessage("Title must not exceed 500 characters.");

        RuleFor(x => x.Domain)
            .NotEmpty().WithMessage("Domain is required.")
            .MaximumLength(100).WithMessage("Domain must not exceed 100 characters.");

        RuleFor(x => x.RequiredMajorIds)
            .NotNull().WithMessage("RequiredMajorIds cannot be null.")
            .Must(x => x.Distinct().Count() == x.Count).WithMessage("RequiredMajorIds must not contain duplicate values.");

        RuleForEach(x => x.Technologies)
            .NotEmpty().WithMessage("Technology tag must not be empty.")
            .MaximumLength(100).WithMessage("Technology tag must not exceed 100 characters.");

        RuleFor(x => x.Technologies)
            .Must(x => x.Select(t => t.Trim().ToLowerInvariant()).Distinct().Count() == x.Count)
            .WithMessage("Technologies must not contain duplicate tags.");

        RuleForEach(x => x.Keywords)
            .NotEmpty().WithMessage("Keyword tag must not be empty.")
            .MaximumLength(100).WithMessage("Keyword tag must not exceed 100 characters.");

        RuleFor(x => x.Keywords)
            .Must(x => x.Select(k => k.Trim().ToLowerInvariant()).Distinct().Count() == x.Count)
            .WithMessage("Keywords must not contain duplicate tags.");
    }
}

public sealed class SetProjectMajorsCommandValidator : AbstractValidator<SetProjectMajorsCommand>
{
    public SetProjectMajorsCommandValidator()
    {
        RuleFor(x => x.ProjectId)
            .GreaterThan(0).WithMessage("ProjectId must be greater than 0.");

        RuleFor(x => x.ConcurrencyToken)
            .NotEmpty().WithMessage("ConcurrencyToken is required.");

        RuleFor(x => x.RequiredMajorIds)
            .NotEmpty().WithMessage("RequiredMajorIds must not be empty.")
            .Must(x => x.Distinct().Count() == x.Count).WithMessage("RequiredMajorIds must not contain duplicate values.");
    }
}

public sealed class SubmitProjectCommandValidator : AbstractValidator<SubmitProjectCommand>
{
    public SubmitProjectCommandValidator()
    {
        RuleFor(x => x.ProjectId)
            .GreaterThan(0).WithMessage("ProjectId must be greater than 0.");

        RuleFor(x => x.ConcurrencyToken)
            .NotEmpty().WithMessage("ConcurrencyToken is required.");
    }
}

public sealed class ResubmitProjectCommandValidator : AbstractValidator<ResubmitProjectCommand>
{
    public ResubmitProjectCommandValidator()
    {
        RuleFor(x => x.ProjectId)
            .GreaterThan(0).WithMessage("ProjectId must be greater than 0.");

        RuleFor(x => x.ConcurrencyToken)
            .NotEmpty().WithMessage("ConcurrencyToken is required.");
    }
}

public sealed class StartReviewProjectCommandValidator : AbstractValidator<StartReviewProjectCommand>
{
    public StartReviewProjectCommandValidator()
    {
        RuleFor(x => x.ProjectId)
            .GreaterThan(0).WithMessage("ProjectId must be greater than 0.");

        RuleFor(x => x.ConcurrencyToken)
            .NotEmpty().WithMessage("ConcurrencyToken is required.");
    }
}

public sealed class ApproveProjectCommandValidator : AbstractValidator<ApproveProjectCommand>
{
    public ApproveProjectCommandValidator()
    {
        RuleFor(x => x.ProjectId)
            .GreaterThan(0).WithMessage("ProjectId must be greater than 0.");

        RuleFor(x => x.ConcurrencyToken)
            .NotEmpty().WithMessage("ConcurrencyToken is required.");
    }
}

public sealed class RejectProjectCommandValidator : AbstractValidator<RejectProjectCommand>
{
    public RejectProjectCommandValidator()
    {
        RuleFor(x => x.ProjectId)
            .GreaterThan(0).WithMessage("ProjectId must be greater than 0.");

        RuleFor(x => x.ConcurrencyToken)
            .NotEmpty().WithMessage("ConcurrencyToken is required.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Reason is required.")
            .MaximumLength(1000).WithMessage("Reason must not exceed 1000 characters.");
    }
}

public sealed class RequestProjectRevisionCommandValidator : AbstractValidator<RequestProjectRevisionCommand>
{
    public RequestProjectRevisionCommandValidator()
    {
        RuleFor(x => x.ProjectId)
            .GreaterThan(0).WithMessage("ProjectId must be greater than 0.");

        RuleFor(x => x.ConcurrencyToken)
            .NotEmpty().WithMessage("ConcurrencyToken is required.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Reason is required.")
            .MaximumLength(1000).WithMessage("Reason must not exceed 1000 characters.");
    }
}

public sealed class GetProjectByIdQueryValidator : AbstractValidator<GetProjectByIdQuery>
{
    public GetProjectByIdQueryValidator()
    {
        RuleFor(x => x.Id)
            .GreaterThan(0).WithMessage("Project ID must be greater than 0.");
    }
}

public sealed class GetProjectStatusHistoryQueryValidator : AbstractValidator<GetProjectStatusHistoryQuery>
{
    public GetProjectStatusHistoryQueryValidator()
    {
        RuleFor(x => x.ProjectId)
            .GreaterThan(0).WithMessage("ProjectId must be greater than 0.");
    }
}

public sealed class GetProjectsQueryValidator : AbstractValidator<GetProjectsQuery>
{
    public GetProjectsQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1).WithMessage("Page number must be greater than or equal to 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).WithMessage("PageSize must be between 1 and 100.");
    }
}

public sealed class GetProjectReviewQueueQueryValidator : AbstractValidator<GetProjectReviewQueueQuery>
{
    public GetProjectReviewQueueQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1).WithMessage("Page number must be greater than or equal to 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).WithMessage("PageSize must be between 1 and 100.");
    }
}
