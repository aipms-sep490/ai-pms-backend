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
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
public sealed class StudentQualificationsController(ISender sender) : ControllerBase
{
    [HttpPost("me/certificate")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(22 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 22 * 1024 * 1024)]
    [ProducesResponseType<StudentQualificationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<StudentQualificationDto>> UploadCertificate([FromForm] QualificationCertificateForm request, CancellationToken ct)
    {
        await using var content = request.File.OpenReadStream();
        return Ok(await sender.Send(new UploadStudentQualificationCertificateCommand(
            new(request.QualificationType, request.TrainingStatus, request.CertificateNumber, null, request.IssuedAt, request.ExpiresAt),
            new(request.File.FileName, request.File.ContentType, request.File.Length, content)), ct));
    }

    [HttpGet("me")]
    [ProducesResponseType<StudentQualificationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<StudentQualificationDto>> Mine(
        [FromQuery] string qualificationType = StudentQualificationTypes.CapstoneReadiness,
        CancellationToken ct = default)
    {
        var result = await sender.Send(new GetMyStudentQualificationQuery(qualificationType), ct);
        return result is null ? NoContent() : Ok(result);
    }

    [HttpGet("{qualificationId:long}/certificate")]
    [ProducesResponseType<StudentQualificationCertificateDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<StudentQualificationCertificateDto>> Certificate(long qualificationId, CancellationToken ct) =>
        Ok(await sender.Send(new GetStudentQualificationCertificateQuery(qualificationId), ct));

    [HttpGet("{qualificationId:long}/certificate/download")]
    [ProducesResponseType<FileStreamResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CertificateDownload(long qualificationId, CancellationToken ct)
    {
        var file = await sender.Send(new DownloadStudentQualificationCertificateQuery(qualificationId), ct);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.CacheControl = "no-store";
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpPost("me/evidence")]
    [ProducesResponseType<StudentQualificationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<StudentQualificationDto>> SubmitEvidence(
        SubmitStudentQualificationEvidenceRequest request,
        CancellationToken ct) =>
        Ok(await sender.Send(new SubmitStudentQualificationEvidenceCommand(request), ct));

    [HttpGet("verification-queue")]
    [ProducesResponseType<PagedResult<StudentQualificationDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<StudentQualificationDto>>> VerificationQueue(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default) =>
        Ok(await sender.Send(new GetStudentQualificationVerificationQueueQuery(
            status, search, page, pageSize), ct));

    [HttpPost("{qualificationId:long}/verify")]
    [ProducesResponseType<StudentQualificationDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<StudentQualificationDto>> Verify(
        long qualificationId,
        [FromBody] VerifyStudentQualificationRequest? request,
        CancellationToken ct) =>
        Ok(await sender.Send(new VerifyStudentQualificationCommand(qualificationId, request?.ExpectedConcurrencyToken), ct));

    [HttpPost("{qualificationId:long}/reject")]
    [ProducesResponseType<StudentQualificationDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<StudentQualificationDto>> Reject(
        long qualificationId,
        DecideStudentQualificationRequest request,
        CancellationToken ct) =>
        Ok(await sender.Send(new RejectStudentQualificationCommand(
            qualificationId, request.Reason, request.ExpectedConcurrencyToken), ct));

    [HttpGet("/api/v1/academic/project-periods/{projectPeriodId:long}/qualification-policy")]
    [ProducesResponseType<ProjectPeriodQualificationPolicyDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectPeriodQualificationPolicyDto>> GetPolicy(
        long projectPeriodId,
        CancellationToken ct) =>
        Ok(await sender.Send(new GetProjectPeriodQualificationPolicyQuery(projectPeriodId), ct));

    [HttpPut("/api/v1/academic/project-periods/{projectPeriodId:long}/qualification-policy")]
    [ProducesResponseType<ProjectPeriodQualificationPolicyDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectPeriodQualificationPolicyDto>> SetPolicy(
        long projectPeriodId,
        SetProjectPeriodQualificationPolicyRequest request,
        CancellationToken ct) =>
        Ok(await sender.Send(new SetProjectPeriodQualificationPolicyCommand(
            projectPeriodId, request), ct));
}

public sealed class QualificationCertificateForm
{
    [System.ComponentModel.DataAnnotations.Required] public IFormFile File { get; init; } = null!;
    [System.ComponentModel.DataAnnotations.Required] public string QualificationType { get; init; } = "CAPSTONE_READINESS";
    [System.ComponentModel.DataAnnotations.Required] public string TrainingStatus { get; init; } = "TRAINING_COMPLETED";
    public string? CertificateNumber { get; init; }
    public DateTime? IssuedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
}
