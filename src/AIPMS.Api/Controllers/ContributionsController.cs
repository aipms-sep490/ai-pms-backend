using System.Collections.Generic;
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
    public Task<IReadOnlyList<ContributionMemberDto>> Get(long projectId, CancellationToken ct) =>
        sender.Send(new GetProjectContributionQuery(projectId), ct);
}
