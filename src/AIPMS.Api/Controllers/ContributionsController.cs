using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Features.Contributions.DTOs;
using AIPMS.Application.Features.Contributions.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController, Authorize]
[Route("api/v1/projects/{projectId:long}/contributions")]
public sealed class ContributionsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public Task<ContributionSummaryDto> Get(long projectId, CancellationToken ct) =>
        sender.Send(new GetProjectContributionQuery(projectId), ct);

    [HttpGet("{userId:long}/evidence")]
    public Task<IReadOnlyList<ContributionEvidenceDto>> Evidence(long projectId, long userId, CancellationToken ct) =>
        sender.Send(new GetContributionEvidenceQuery(projectId, userId), ct);

    [HttpPost("snapshot")]
    public Task<ContributionSummaryDto> RebuildSnapshot(long projectId, CancellationToken ct) =>
        sender.Send(new RebuildContributionSnapshotCommand(projectId), ct);
}
