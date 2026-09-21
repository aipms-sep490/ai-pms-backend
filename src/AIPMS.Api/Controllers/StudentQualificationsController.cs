using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.StudentQualifications.Commands;
using AIPMS.Application.Features.StudentQualifications.DTOs;
using AIPMS.Application.Features.StudentQualifications.Models;
using AIPMS.Application.Features.StudentQualifications.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/student-qualifications")]
public sealed class StudentQualificationsController(ISender sender) : ControllerBase
{
    [HttpGet("me")]
    public async Task<ActionResult<StudentQualificationDto>> Mine(
        [FromQuery] string qualificationType = StudentQualificationTypes.CapstoneReadiness,
        CancellationToken ct = default)
    {
        var result = await sender.Send(new GetMyStudentQualificationQuery(qualificationType), ct);
        return result is null ? NoContent() : Ok(result);
    }

    [HttpPost("me/evidence")]
    public async Task<ActionResult<StudentQualificationDto>> SubmitEvidence(
        SubmitStudentQualificationEvidenceRequest request,
        CancellationToken ct) =>
        Ok(await sender.Send(new SubmitStudentQualificationEvidenceCommand(request), ct));

    [HttpGet("verification-queue")]
    public async Task<ActionResult<PagedResult<StudentQualificationDto>>> VerificationQueue(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default) =>
        Ok(await sender.Send(new GetStudentQualificationVerificationQueueQuery(
            status, search, page, pageSize), ct));

    [HttpPost("{qualificationId:long}/verify")]
    public async Task<ActionResult<StudentQualificationDto>> Verify(
        long qualificationId,
        CancellationToken ct) =>
        Ok(await sender.Send(new VerifyStudentQualificationCommand(qualificationId), ct));

    [HttpPost("{qualificationId:long}/reject")]
    public async Task<ActionResult<StudentQualificationDto>> Reject(
        long qualificationId,
        DecideStudentQualificationRequest request,
        CancellationToken ct) =>
        Ok(await sender.Send(new RejectStudentQualificationCommand(
            qualificationId, request.Reason), ct));

    [HttpGet("/api/v1/academic/project-periods/{projectPeriodId:long}/qualification-policy")]
    public async Task<ActionResult<ProjectPeriodQualificationPolicyDto>> GetPolicy(
        long projectPeriodId,
        CancellationToken ct) =>
        Ok(await sender.Send(new GetProjectPeriodQualificationPolicyQuery(projectPeriodId), ct));

    [HttpPut("/api/v1/academic/project-periods/{projectPeriodId:long}/qualification-policy")]
    public async Task<ActionResult<ProjectPeriodQualificationPolicyDto>> SetPolicy(
        long projectPeriodId,
        SetProjectPeriodQualificationPolicyRequest request,
        CancellationToken ct) =>
        Ok(await sender.Send(new SetProjectPeriodQualificationPolicyCommand(
            projectPeriodId, request), ct));
}
