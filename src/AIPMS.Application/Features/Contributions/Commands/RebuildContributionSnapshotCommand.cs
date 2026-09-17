using AIPMS.Application.Features.Contributions.DTOs;
using AIPMS.Application.Features.Contributions.Services;
using MediatR;

namespace AIPMS.Application.Features.Contributions.Commands;

public sealed record RebuildContributionSnapshotCommand(long ProjectId) : IRequest<ContributionSummaryDto>;

public sealed class RebuildContributionSnapshotCommandHandler(ContributionWorkflow workflow)
    : IRequestHandler<RebuildContributionSnapshotCommand, ContributionSummaryDto>
{
    public Task<ContributionSummaryDto> Handle(RebuildContributionSnapshotCommand request, CancellationToken ct) =>
        workflow.Rebuild(request.ProjectId, ct);
}
