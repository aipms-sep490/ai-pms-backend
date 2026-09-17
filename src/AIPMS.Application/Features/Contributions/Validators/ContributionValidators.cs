using AIPMS.Application.Features.Contributions.Commands;
using AIPMS.Application.Features.Contributions.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.Contributions.Validators;

public sealed class GetProjectContributionQueryValidator : AbstractValidator<GetProjectContributionQuery>
{
    public GetProjectContributionQueryValidator()
    {
        RuleFor(x => x.ProjectId).GreaterThan(0);
        RuleFor(x => x.Page).InclusiveBetween(1, 1000000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class GetContributionEvidenceQueryValidator : AbstractValidator<GetContributionEvidenceQuery>
{
    public GetContributionEvidenceQueryValidator()
    {
        RuleFor(x => x.ProjectId).GreaterThan(0);
        RuleFor(x => x.UserId).GreaterThan(0);
        RuleFor(x => x.Page).InclusiveBetween(1, 1000000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.SourceType).Must(s => s is null or "TASK" or "PROGRESS_REPORT" or "MEETING" or "DELIVERABLE_VERSION" or "FILE");
    }
}

public sealed class RebuildContributionSnapshotCommandValidator : AbstractValidator<RebuildContributionSnapshotCommand>
{
    public RebuildContributionSnapshotCommandValidator() => RuleFor(x => x.ProjectId).GreaterThan(0);
}
