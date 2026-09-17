using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Features.Contributions.DTOs;
using AIPMS.Application.Features.Contributions.Queries;
using AIPMS.Application.Features.Contributions.Commands;
using AIPMS.Application.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController, Authorize]
[Route("api/v1/projects/{projectId:long}/contributions")]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(409)]
public sealed class ContributionsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public Task<ContributionSummaryDto> Get(long projectId, CancellationToken ct,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] bool snapshot = false) =>
        sender.Send(new GetProjectContributionQuery(projectId, page, pageSize, snapshot), ct);

    [HttpGet("{userId:long}/evidence")]
    public Task<PagedResult<ContributionEvidenceDto>> Evidence(long projectId, long userId, CancellationToken ct,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? sourceType = null) =>
        sender.Send(new GetContributionEvidenceQuery(projectId, userId, page, pageSize, sourceType), ct);

    [HttpPost("snapshot")]
    public Task<ContributionSummaryDto> RebuildSnapshot(long projectId, CancellationToken ct) =>
        sender.Send(new RebuildContributionSnapshotCommand(projectId), ct);
}
