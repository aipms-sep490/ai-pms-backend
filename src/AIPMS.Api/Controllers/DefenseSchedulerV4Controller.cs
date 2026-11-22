using AIPMS.Application.Features.AcademicTracking;
using AIPMS.Application.Features.AcademicTracking.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController, Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(409)]
[ProducesResponseType<ProblemDetails>(422)]
public sealed class DefenseSchedulerV4Controller(IDefenseSchedulerV4Service service) : ControllerBase
{
    [HttpPatch("api/v1/academic/project-periods/{periodId:long}/defense-room-scheduling")]
    [ProducesResponseType<PeriodDefenseRoomSchedulingDto>(200)]
    public Task<PeriodDefenseRoomSchedulingDto> UpdatePeriodSchedulingConfig(
        long periodId, UpdatePeriodDefenseRoomSchedulingRequest input, CancellationToken ct) =>
        service.UpdatePeriodSchedulingConfigAsync(periodId, input, ct);

    [HttpPost("api/v1/defense-scheduling/v4/tech-checks")]
    [ProducesResponseType<DefenseTechCheckDto>(201)]
    public async Task<ActionResult<DefenseTechCheckDto>> RecordTechCheck(RecordDefenseTechCheckRequest input, CancellationToken ct) =>
        StatusCode(201, await service.RecordTechCheckAsync(input, ct));

    [HttpPost("api/v1/defense-scheduling/v4/sessions")]
    [ProducesResponseType<DefenseSessionV4Dto>(201)]
    public async Task<ActionResult<DefenseSessionV4Dto>> ScheduleSessionV4(ScheduleDefenseV4Request input, CancellationToken ct) =>
        StatusCode(201, await service.ScheduleSessionV4Async(input, ct));

    [HttpPost("api/v1/defense-scheduling/v4/change-requests")]
    [ProducesResponseType<DefenseChangeProposalV4Dto>(200)]
    public Task<DefenseChangeProposalV4Dto> ProposeChangeV4(CreateDefenseChangeProposalV4Request input, CancellationToken ct) =>
        service.ProposeChangeV4Async(input, ct);

    [HttpPost("api/v1/defense-scheduling/v4/standby-pool")]
    [ProducesResponseType<StandbyPoolMemberDto>(200)]
    public Task<StandbyPoolMemberDto> AddStandbyPoolMember(AddStandbyPoolMemberRequest input, CancellationToken ct) =>
        service.AddStandbyPoolMemberAsync(input, ct);

    [HttpGet("api/v1/defense-scheduling/v4/standby-pool")]
    [ProducesResponseType<IReadOnlyList<StandbyPoolMemberDto>>(200)]
    public Task<IReadOnlyList<StandbyPoolMemberDto>> ListStandbyPool([FromQuery] long periodId, CancellationToken ct) =>
        service.ListStandbyPoolAsync(periodId, ct);

    [HttpPost("api/v1/defense-scheduling/v4/sessions/{sessionId:long}/standby-swap")]
    [ProducesResponseType<StandbySwapResultDto>(200)]
    public Task<StandbySwapResultDto> StandbySwap(long sessionId, StandbySwapRequest input, CancellationToken ct) =>
        service.StandbySwapAsync(sessionId, input, ct);
}
