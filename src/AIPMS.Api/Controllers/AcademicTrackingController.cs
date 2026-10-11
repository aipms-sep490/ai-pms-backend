using AIPMS.Application.Common.Models;
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
public sealed class AcademicTrackingController(IAcademicTrackingService service) : ControllerBase
{
    [HttpGet("api/v1/project-evidence/{id:long}/review-target")]
    [ProducesResponseType<EvidenceReviewTarget>(200)]
    public Task<EvidenceReviewTarget> Target(long id, [FromQuery] long? sourceVersionId, CancellationToken ct) => service.ReviewTarget(id, sourceVersionId, ct);
    [HttpPost("api/v1/project-evidence/{id:long}/reviews")]
    [ProducesResponseType<EvidenceReviewDto>(200)]
    public Task<EvidenceReviewDto> Review(long id, ReviewEvidenceRequest request, CancellationToken ct) => service.Review(id, request, ct);
    [HttpGet("api/v1/project-evidence/{id:long}/reviews")]
    [ProducesResponseType<PagedResult<EvidenceReviewDto>>(200)]
    public Task<PagedResult<EvidenceReviewDto>> Reviews(long id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => service.Reviews(id, page, pageSize, ct);
    [HttpPost("api/v1/projects/{projectId:long}/checkpoints")]
    [ProducesResponseType<CheckpointDto>(200)]
    public Task<CheckpointDto> CreateCheckpoint(long projectId, SaveCheckpointRequest request, CancellationToken ct) => service.SaveCheckpoint(projectId, null, request, ct);
    [HttpPut("api/v1/projects/{projectId:long}/checkpoints/{id:long}")]
    [ProducesResponseType<CheckpointDto>(200)]
    public Task<CheckpointDto> UpdateCheckpoint(long projectId, long id, SaveCheckpointRequest request, CancellationToken ct) => service.SaveCheckpoint(projectId, id, request, ct);
    [HttpGet("api/v1/projects/{projectId:long}/checkpoints")]
    [ProducesResponseType<PagedResult<CheckpointDto>>(200)]
    public Task<PagedResult<CheckpointDto>> Checkpoints(long projectId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => service.Checkpoints(projectId, page, pageSize, ct);
    [HttpPost("api/v1/checkpoints/{id:long}/decisions")]
    [ProducesResponseType<CheckpointDto>(200)]
    public Task<CheckpointDto> DecideCheckpoint(long id, DecideCheckpointRequest request, CancellationToken ct) => service.DecideCheckpoint(id, request, ct);
    [HttpPost("api/v1/projects/{projectId:long}/defense-committees")]
    [ProducesResponseType<CommitteeDto>(200)]
    public Task<CommitteeDto> CreateCommittee(long projectId, CreateCommitteeRequest request, CancellationToken ct) => service.CreateCommittee(projectId, request, ct);
    [HttpGet("api/v1/projects/{projectId:long}/defense-committees")]
    [ProducesResponseType<PagedResult<CommitteeDto>>(200)]
    public Task<PagedResult<CommitteeDto>> Committees(long projectId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => service.Committees(projectId, page, pageSize, ct);
    [HttpPost("api/v1/projects/{projectId:long}/defense-sessions")]
    [ProducesResponseType<DefenseSessionDto>(200)]
    public Task<DefenseSessionDto> ScheduleDefense(long projectId, ScheduleDefenseRequest request, CancellationToken ct) => service.ScheduleDefense(projectId, request, ct);
    [HttpGet("api/v1/projects/{projectId:long}/defense-sessions")]
    [ProducesResponseType<PagedResult<DefenseSessionDto>>(200)]
    public Task<PagedResult<DefenseSessionDto>> DefenseSessions(long projectId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => service.DefenseSessions(projectId, page, pageSize, ct);
    [HttpPost("api/v1/defense-sessions/{id:long}/cancel")]
    [ProducesResponseType<DefenseSessionDto>(200)]
    public Task<DefenseSessionDto> CancelDefense(long id, CancelDefenseRequest request, CancellationToken ct) => service.CancelDefense(id, request, ct);
    [HttpGet("api/v1/defense-sessions/{id:long}")]
    [ProducesResponseType<DefenseSessionDto>(200)]
    public Task<DefenseSessionDto> DefenseSession(long id, CancellationToken ct) => service.DefenseSession(id, ct);
}
