using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.AccountSecurity.DTOs;
using AIPMS.Application.Features.AccountSecurity.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.AccountSecurityManagement)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/v1/security/access-events")]
public sealed class AccessEventsController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<AccessEventDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AccessEventDto>>> Get(
        [FromQuery] string? search,
        [FromQuery] long? userId,
        [FromQuery] string? eventType,
        [FromQuery] string? outcome,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await sender.Send(new GetAccessEventsQuery(
            search, userId, eventType, outcome, fromUtc, toUtc, page, pageSize), cancellationToken));
}
