using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Features.AcademicBlueprints;
using AIPMS.Application.Features.AcademicBlueprints.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
public sealed class AcademicBlueprintsController(
    IAcademicBlueprintService service,
    ICurrentUser currentUser,
    System.TimeProvider clock) : ControllerBase
{
    [HttpGet("api/v1/academic-blueprints")]
    public async Task<ActionResult<IReadOnlyList<AcademicBlueprintDto>>> List(
        [FromQuery] long? semesterId,
        [FromQuery] string? trackCode,
        [FromQuery] string? status,
        CancellationToken ct) =>
        Ok(await service.ListAsync(semesterId, trackCode, status, ct));

    [HttpGet("api/v1/academic-blueprints/{id:long}")]
    public async Task<ActionResult<AcademicBlueprintDto>> Get(long id, CancellationToken ct) =>
        Ok(await service.GetAsync(id, ct));

    [HttpPost("api/v1/academic-blueprints")]
    public async Task<ActionResult<AcademicBlueprintDto>> Create([FromBody] CreateAcademicBlueprintRequest request, CancellationToken ct)
    {
        var created = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("api/v1/academic-blueprints/{id:long}")]
    public async Task<ActionResult<AcademicBlueprintDto>> Update(long id, [FromBody] UpdateAcademicBlueprintRequest request, CancellationToken ct) =>
        Ok(await service.UpdateAsync(id, request, ct));

    [HttpPost("api/v1/academic-blueprints/{id:long}/clone")]
    public async Task<ActionResult<AcademicBlueprintDto>> Clone(long id, CancellationToken ct) =>
        Ok(await service.CloneVersionAsync(id, ct));

    [HttpPost("api/v1/academic-blueprints/{id:long}/submit-review")]
    public async Task<ActionResult<AcademicBlueprintDto>> SubmitForReview(long id, CancellationToken ct) =>
        Ok(await service.SubmitForReviewAsync(id, ct));

    [HttpPost("api/v1/academic-blueprints/{id:long}/approvals")]
    public async Task<ActionResult<AcademicBlueprintDto>> RecordApproval(long id, [FromBody] RecordBlueprintApprovalRequest request, CancellationToken ct) =>
        Ok(await service.RecordApprovalAsync(id, request, ct));

    [HttpPost("api/v1/academic-blueprints/{id:long}/publish")]
    public async Task<ActionResult<AcademicBlueprintDto>> Publish(long id, CancellationToken ct) =>
        Ok(await service.PublishAsync(id, ct));

    [HttpGet("api/v1/semesters/{semesterId:long}/blueprint-readiness")]
    public async Task<ActionResult<SemesterReadinessDto>> GetSemesterReadiness(long semesterId, CancellationToken ct) =>
        Ok(await service.GetSemesterReadinessAsync(semesterId, ct));

    [HttpGet("api/v1/academic/semesters/{semesterId:long}/track-presets")]
    public async Task<ActionResult<IReadOnlyList<SemesterTrackPresetDto>>> GetSemesterTrackPresets(long semesterId, CancellationToken ct) =>
        Ok(await service.GetSemesterTrackPresetsAsync(semesterId, ct));

    [HttpPost("api/v1/projects/{projectId:long}/blueprint-plan/dry-run")]
    public async Task<ActionResult<BlueprintDryRunDiffDto>> DryRunPlan(long projectId, [FromQuery] long? blueprintId, CancellationToken ct) =>
        Ok(await service.DryRunProjectPlanAsync(projectId, blueprintId, ct));

    [HttpPost("api/v1/projects/{projectId:long}/blueprint-plan/apply")]
    public async Task<ActionResult<ProjectBlueprintSnapshotDto>> ApplyPlan(long projectId, [FromQuery] long? blueprintId, CancellationToken ct)
    {
        var actorId = currentUser.UserId ?? throw new AIPMS.Application.Common.Exceptions.UnauthorizedException();
        var isStaff = currentUser.Roles.Contains(AIPMS.Application.Common.Security.AppRoles.DepartmentStaff);
        if (!isStaff)
        {
            var isAdmin = currentUser.Roles.Contains(AIPMS.Application.Common.Security.AppRoles.Admin);
            throw new AIPMS.Application.Common.Exceptions.ForbiddenException(
                isAdmin
                    ? "Platform administrators without DEPARTMENT_STAFF role cannot apply academic blueprints to projects."
                    : "Only department staff can apply academic blueprints to projects.",
                isAdmin ? AIPMS.Application.Common.Exceptions.WorkflowErrorCodes.AdminNotAcademicAuthority : null);
        }
        var result = await service.ApplyBlueprintToProjectAsync(projectId, actorId, clock.GetUtcNow().UtcDateTime, blueprintId, ct);
        return Ok(result);
    }

    [HttpGet("api/v1/projects/{projectId:long}/deadline-change-requests")]
    public async Task<ActionResult<IReadOnlyList<GateDeadlineChangeRequestDto>>> ListAllDeadlineChanges(long projectId, CancellationToken ct) =>
        Ok(await service.ListDeadlineChangeRequestsAsync(projectId, null, ct));

    [HttpGet("api/v1/projects/{projectId:long}/review-gates/{gateCode}/deadline-change-requests")]
    public async Task<ActionResult<IReadOnlyList<GateDeadlineChangeRequestDto>>> ListDeadlineChanges(long projectId, string gateCode, CancellationToken ct) =>
        Ok(await service.ListDeadlineChangeRequestsAsync(projectId, gateCode, ct));

    [HttpPost("api/v1/projects/{projectId:long}/review-gates/{gateCode}/deadline-change-requests")]
    public async Task<ActionResult<GateDeadlineChangeRequestDto>> RequestDeadlineChange(
        long projectId,
        string gateCode,
        [FromBody] CreateGateDeadlineChangeRequest request,
        CancellationToken ct) =>
        Ok(await service.CreateDeadlineChangeRequestAsync(projectId, gateCode, request, ct));

    [HttpPost("api/v1/projects/{projectId:long}/deadline-change-requests/{requestId:long}/review")]
    public async Task<ActionResult<GateDeadlineChangeRequestDto>> ReviewDeadlineChange(
        long projectId,
        long requestId,
        [FromBody] ReviewGateDeadlineChangeRequest request,
        CancellationToken ct) =>
        Ok(await service.ReviewDeadlineChangeRequestAsync(projectId, requestId, request, ct));
}
