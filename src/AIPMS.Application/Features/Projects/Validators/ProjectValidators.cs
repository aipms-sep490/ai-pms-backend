using System;
using System.Linq;
using FluentValidation;
using AIPMS.Application.Features.Projects.Commands;
using AIPMS.Application.Features.Projects.Queries;

namespace AIPMS.Application.Features.Projects.Validators;

internal static class ProjectValidationHelpers
{
    public static bool BeValidBase64(string representation)
    {
        if (string.IsNullOrWhiteSpace(representation)) return false;
        var buffer = new byte[representation.Length];
        if (Convert.TryFromBase64String(representation, buffer, out var bytesWritten))
        {
            return bytesWritten == 8;
        }
        return false;
    }
}

public sealed class CreateProjectDraftCommandValidator : AbstractValidator<CreateProjectDraftCommand>
{
    public CreateProjectDraftCommandValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .Must(x => !string.IsNullOrWhiteSpace(x)).WithMessage("Title must not be whitespace.")
            .MaximumLength(500).WithMessage("Title must not exceed 500 characters.");

        RuleFor(x => x.Domain)
            .NotEmpty().WithMessage("Domain is required.")
            .Must(x => !string.IsNullOrWhiteSpace(x)).WithMessage("Domain must not be whitespace.")
            .MaximumLength(100).WithMessage("Domain must not exceed 100 characters.");

        RuleFor(x => x.RequiredMajorIds)
            .NotNull().WithMessage("RequiredMajorIds cannot be null.")
            .Must(x => x == null || x.Distinct().Count() == x.Count).WithMessage("RequiredMajorIds must not contain duplicate values.");

        RuleForEach(x => x.RequiredMajorIds)
            .Must((cmd, id) => id > 0).WithMessage("Each Major ID must be greater than 0.");

        RuleFor(x => x.Technologies)
            .NotNull().WithMessage("Technologies list cannot be null.")
            .Must(x => x == null || x.Select(t => t?.Trim().ToUpperInvariant().Replace(" ", "_") ?? "").Distinct().Count() == x.Count)
            .WithMessage("Technologies must not contain duplicate tags after normalization.");

        RuleForEach(x => x.Technologies)
            .NotEmpty().WithMessage("Technology tag must not be empty.")
            .Must(x => !string.IsNullOrWhiteSpace(x)).WithMessage("Technology tag must not be whitespace.")
            .MaximumLength(100).WithMessage("Technology tag must not exceed 100 characters.");

        RuleFor(x => x.Keywords)
            .NotNull().WithMessage("Keywords list cannot be null.")
            .Must(x => x == null || x.Select(k => k?.Trim().ToUpperInvariant().Replace(" ", "_") ?? "").Distinct().Count() == x.Count)
            .WithMessage("Keywords must not contain duplicate tags after normalization.");

        RuleForEach(x => x.Keywords)
            .NotEmpty().WithMessage("Keyword tag must not be empty.")
            .Must(x => !string.IsNullOrWhiteSpace(x)).WithMessage("Keyword tag must not be whitespace.")
            .MaximumLength(100).WithMessage("Keyword tag must not exceed 100 characters.");
    }
}

public sealed class UpdateProjectDraftCommandValidator : AbstractValidator<UpdateProjectDraftCommand>
{
    public UpdateProjectDraftCommandValidator()
    {
        RuleFor(x => x.ProjectId)
            .GreaterThan(0).WithMessage("ProjectId must be greater than 0.");

        RuleFor(x => x.ConcurrencyToken)
            .NotEmpty().WithMessage("ConcurrencyToken is required.")
            .Must(ProjectValidationHelpers.BeValidBase64).WithMessage("ConcurrencyToken must be a valid 8-byte Base64 string.");

        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .Must(x => !string.IsNullOrWhiteSpace(x)).WithMessage("Title must not be whitespace.")
            .MaximumLength(500).WithMessage("Title must not exceed 500 characters.");

        RuleFor(x => x.Domain)
            .NotEmpty().WithMessage("Domain is required.")
            .Must(x => !string.IsNullOrWhiteSpace(x)).WithMessage("Domain must not be whitespace.")
            .MaximumLength(100).WithMessage("Domain must not exceed 100 characters.");

        RuleFor(x => x.RequiredMajorIds)
            .NotNull().WithMessage("RequiredMajorIds cannot be null.")
            .Must(x => x == null || x.Distinct().Count() == x.Count).WithMessage("RequiredMajorIds must not contain duplicate values.");

        RuleForEach(x => x.RequiredMajorIds)
            .Must((cmd, id) => id > 0).WithMessage("Each Major ID must be greater than 0.");

        RuleFor(x => x.Technologies)
            .NotNull().WithMessage("Technologies list cannot be null.")
            .Must(x => x == null || x.Select(t => t?.Trim().ToUpperInvariant().Replace(" ", "_") ?? "").Distinct().Count() == x.Count)
            .WithMessage("Technologies must not contain duplicate tags after normalization.");

        RuleForEach(x => x.Technologies)
            .NotEmpty().WithMessage("Technology tag must not be empty.")
            .Must(x => !string.IsNullOrWhiteSpace(x)).WithMessage("Technology tag must not be whitespace.")
            .MaximumLength(100).WithMessage("Technology tag must not exceed 100 characters.");

        RuleFor(x => x.Keywords)
            .NotNull().WithMessage("Keywords list cannot be null.")
            .Must(x => x == null || x.Select(k => k?.Trim().ToUpperInvariant().Replace(" ", "_") ?? "").Distinct().Count() == x.Count)
            .WithMessage("Keywords must not contain duplicate tags after normalization.");

        RuleForEach(x => x.Keywords)
            .NotEmpty().WithMessage("Keyword tag must not be empty.")
            .Must(x => !string.IsNullOrWhiteSpace(x)).WithMessage("Keyword tag must not be whitespace.")
            .MaximumLength(100).WithMessage("Keyword tag must not exceed 100 characters.");
    }
}

public sealed class SetProjectMajorsCommandValidator : AbstractValidator<SetProjectMajorsCommand>
{
    public SetProjectMajorsCommandValidator()
    {
        RuleFor(x => x.ProjectId)
            .GreaterThan(0).WithMessage("ProjectId must be greater than 0.");

        RuleFor(x => x.ConcurrencyToken)
            .NotEmpty().WithMessage("ConcurrencyToken is required.")
            .Must(ProjectValidationHelpers.BeValidBase64).WithMessage("ConcurrencyToken must be a valid 8-byte Base64 string.");

        RuleFor(x => x.RequiredMajorIds)
            .NotEmpty().WithMessage("RequiredMajorIds must not be empty.")
            .Must(x => x == null || x.Distinct().Count() == x.Count).WithMessage("RequiredMajorIds must not contain duplicate values.");

        RuleForEach(x => x.RequiredMajorIds)
            .Must((cmd, id) => id > 0).WithMessage("Each Major ID must be greater than 0.");
    }
}

public sealed class SubmitProjectCommandValidator : AbstractValidator<SubmitProjectCommand>
{
    public SubmitProjectCommandValidator()
    {
        RuleFor(x => x.ProjectId)
            .GreaterThan(0).WithMessage("ProjectId must be greater than 0.");

        RuleFor(x => x.ConcurrencyToken)
            .NotEmpty().WithMessage("ConcurrencyToken is required.")
            .Must(ProjectValidationHelpers.BeValidBase64).WithMessage("ConcurrencyToken must be a valid 8-byte Base64 string.");
    }
}

public sealed class ResubmitProjectCommandValidator : AbstractValidator<ResubmitProjectCommand>
{
    public ResubmitProjectCommandValidator()
    {
        RuleFor(x => x.ProjectId)
            .GreaterThan(0).WithMessage("ProjectId must be greater than 0.");

        RuleFor(x => x.ConcurrencyToken)
            .NotEmpty().WithMessage("ConcurrencyToken is required.")
            .Must(ProjectValidationHelpers.BeValidBase64).WithMessage("ConcurrencyToken must be a valid 8-byte Base64 string.");
    }
}

public sealed class StartReviewProjectCommandValidator : AbstractValidator<StartReviewProjectCommand>
{
    public StartReviewProjectCommandValidator()
    {
        RuleFor(x => x.ProjectId)
            .GreaterThan(0).WithMessage("ProjectId must be greater than 0.");

        RuleFor(x => x.ConcurrencyToken)
            .NotEmpty().WithMessage("ConcurrencyToken is required.")
            .Must(ProjectValidationHelpers.BeValidBase64).WithMessage("ConcurrencyToken must be a valid 8-byte Base64 string.");
    }
}

public sealed class ApproveProjectCommandValidator : AbstractValidator<ApproveProjectCommand>
{
    public ApproveProjectCommandValidator()
    {
        RuleFor(x => x.ProjectId)
            .GreaterThan(0).WithMessage("ProjectId must be greater than 0.");

        RuleFor(x => x.ConcurrencyToken)
            .NotEmpty().WithMessage("ConcurrencyToken is required.")
            .Must(ProjectValidationHelpers.BeValidBase64).WithMessage("ConcurrencyToken must be a valid 8-byte Base64 string.");
    }
}

public sealed class RejectProjectCommandValidator : AbstractValidator<RejectProjectCommand>
{
    public RejectProjectCommandValidator()
    {
        RuleFor(x => x.ProjectId)
            .GreaterThan(0).WithMessage("ProjectId must be greater than 0.");

        RuleFor(x => x.ConcurrencyToken)
            .NotEmpty().WithMessage("ConcurrencyToken is required.")
            .Must(ProjectValidationHelpers.BeValidBase64).WithMessage("ConcurrencyToken must be a valid 8-byte Base64 string.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Reason is required.")
            .Must(x => !string.IsNullOrWhiteSpace(x)).WithMessage("Reason must not be whitespace.")
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
            .NotEmpty().WithMessage("ConcurrencyToken is required.")
            .Must(ProjectValidationHelpers.BeValidBase64).WithMessage("ConcurrencyToken must be a valid 8-byte Base64 string.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Reason is required.")
            .Must(x => !string.IsNullOrWhiteSpace(x)).WithMessage("Reason must not be whitespace.")
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
