using AIPMS.Application.Features.AcademicTracking;
using AIPMS.Application.Features.AcademicTracking.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController, Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DefenseChangeRequestsController(IDefenseChangeRequestService changes) : ControllerBase
{
    [HttpPost("api/v1/defense-sessions/{sessionId:long}/change-requests")]
    public async Task<ActionResult<DefenseChangeRequestDto>> Create(long sessionId, CreateDefenseChangeRequest input, CancellationToken ct) =>
        StatusCode(201, await changes.Create(sessionId, input, ct));

    [HttpPost("api/v1/defense-sessions/change-requests/{id:long}/decision")]
    public Task<DefenseChangeRequestDto> Decide(long id, DecideDefenseChangeRequest input, CancellationToken ct) =>
        changes.Decide(id, input, ct);

    [HttpPost("api/v1/defense-sessions/change-requests/{id:long}/withdraw")]
    public Task<DefenseChangeRequestDto> Withdraw(long id, WithdrawDefenseChangeRequest input, CancellationToken ct) =>
        changes.Withdraw(id, input, ct);

    [HttpGet("api/v1/project-periods/{id:long}/defense-sessions/change-requests")]
    public Task<IReadOnlyList<DefenseChangeRequestDto>> List(long id, [FromQuery] string? status, CancellationToken ct) =>
        changes.List(id, status, ct);
}
