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
[Route("api/v1/security/audit-logs")]
public sealed class AuditLogsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<AuditRecordDto>>> GetAuditLogs(
        [FromQuery] long? actorUserId,
        [FromQuery] string? action,
        [FromQuery] string? entityType,
        [FromQuery] string? outcome,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await sender.Send(
            new GetAuditLogsQuery(
                actorUserId,
                action,
                entityType,
                outcome,
                fromUtc,
                toUtc,
                page,
                pageSize),
            cancellationToken));
}
