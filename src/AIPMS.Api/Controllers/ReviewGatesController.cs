using AIPMS.Application.Features.ReviewGates;
using AIPMS.Application.Features.ReviewGates.Abstractions;
using AIPMS.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using AIPMS.Application.Features.Deliverables.Models;
using AIPMS.Application.Features.Deliverables.DTOs;

namespace AIPMS.Api.Controllers;

[ApiController, Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ReviewGatesController(IReviewGateService service) : ControllerBase
{
    [HttpGet("api/v1/academic/gate-templates")]
    public async Task<ActionResult<IReadOnlyList<ReviewGateTemplateDto>>> Templates(CancellationToken ct) => Ok(await service.Templates(ct));
    [HttpGet("api/v1/academic/gate-templates/{id:long}")]
    public async Task<ActionResult<ReviewGateTemplateDto>> Template(long id, CancellationToken ct) => (await service.Template(id, ct)) is { } row ? Ok(row) : NotFound();
    [HttpPost("api/v1/academic/gate-templates")]
    public async Task<ActionResult<ReviewGateTemplateDto>> CreateTemplate(SaveReviewGateTemplateRequest request, CancellationToken ct) => Ok(await service.SaveTemplate(request, null, ct));
    [HttpPut("api/v1/academic/gate-templates/{id:long}")]
    public async Task<ActionResult<ReviewGateTemplateDto>> UpdateTemplate(long id, SaveReviewGateTemplateRequest request, CancellationToken ct) => Ok(await service.SaveTemplate(request, id, ct));
    [HttpPost("api/v1/academic/gate-templates/{id:long}/publish")]
    public async Task<ActionResult<ReviewGateTemplateDto>> PublishTemplate(long id, CancellationToken ct) => Ok(await service.PublishTemplate(id, ct));
    [HttpGet("api/v1/projects/{projectId:long}/review-gates/timeline")]
    public async Task<ActionResult<StudentReviewTimelineDto>> Timeline(long projectId, CancellationToken ct) => Ok(await service.Timeline(projectId, ct));
    [HttpGet("api/v1/projects/{projectId:long}/review-gates")]
    public async Task<ActionResult<StudentReviewTimelineDto>> ProjectReviewGates(long projectId, CancellationToken ct) => Ok(await service.Timeline(projectId, ct));
    [HttpGet("api/v1/students/me/review-gates/timeline")]
    public async Task<ActionResult<StudentReviewTimelineDto>> MyTimeline(CancellationToken ct) => Ok(await service.MyTimeline(ct));
    [HttpPost("api/v1/projects/{projectId:long}/review-gates/{gateCode}/evidence")]
    [Consumes("application/json")]
    public async Task<ActionResult<StudentReviewTimelineDto>> SubmitEvidence(long projectId, string gateCode, SubmitReviewGateEvidenceRequest request, CancellationToken ct) => Ok(await service.SubmitEvidence(projectId, gateCode, request, ct));
    [HttpPost("api/v1/projects/{projectId:long}/review-gates/{gateCode}/evidence")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(22 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 22 * 1024 * 1024)]
    public async Task<ActionResult<StudentReviewTimelineDto>> SubmitEvidenceFile(long projectId, string gateCode, [FromForm] ReviewGateFileForm request, CancellationToken ct)
    {
        await using var stream = request.File.OpenReadStream();
        return Ok(await service.SubmitEvidenceFile(projectId, gateCode,
            new(request.RequirementId, request.Notes, request.ExpectedVersion, request.ConcurrencyToken),
            new(request.File.FileName, request.File.ContentType, request.File.Length, stream), ct));
    }
    [HttpPost("api/v1/projects/{projectId:long}/review-gates/{gateCode}/submit-for-review")]
    public async Task<ActionResult<StudentReviewTimelineDto>> SubmitForReview(long projectId, string gateCode, SubmitReviewGateForReviewRequest request, CancellationToken ct) => Ok(await service.SubmitForReview(projectId, gateCode, request, ct));
    [HttpGet("api/v1/reviewers/me/gate-assignments")]
    public async Task<ActionResult<PagedResult<AssignedReviewItemDto>>> AssignedReviews([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => Ok(await service.AssignedReviews(page, pageSize, ct));
    [HttpGet("api/v1/review-gates/my-assigned-reviews")]
    public async Task<ActionResult<PagedResult<AssignedReviewItemDto>>> MyAssignedReviews([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => Ok(await service.AssignedReviews(page, pageSize, ct));
    [HttpPost("api/v1/review-gates/assignments/{assignmentId:long}/evidence/{requirementId:long}/verify")]
    public async Task<ActionResult<AssignedReviewItemDto>> VerifyEvidence(long assignmentId, string requirementId, VerifyReviewGateEvidenceRequest request, CancellationToken ct) => Ok(await service.VerifyEvidence(assignmentId, request with { RequirementId = requirementId }, ct));
    [HttpPost("api/v1/review-gates/assignments/{assignmentId:long}/decision")]
    public async Task<ActionResult<AssignedReviewItemDto>> Decide(long assignmentId, SubmitReviewGateDecisionRequest request, CancellationToken ct) => Ok(await service.Decide(assignmentId, request, ct));
    [HttpPost("api/v1/review-gates/assignments")]
    public async Task<ActionResult<AssignedReviewItemDto>> Assign(CreateReviewGateAssignmentRequest request, CancellationToken ct) => Ok(await service.Assign(request, ct));
    [HttpPost("api/v1/projects/{projectId:long}/review-gates/{gateCode}/decision")]
    public async Task<ActionResult<ProjectReviewMatrixRowDto>> UpdateDecision(long projectId, string gateCode, UpdateReviewGateDecisionRequest request, CancellationToken ct) => Ok(await service.UpdateDecision(projectId, gateCode, request, ct));
    [HttpPost("api/v1/projects/{projectId:long}/review-gates/scopes/{scope}")]
    public async Task<ActionResult<ProjectReviewMatrixRowDto>> UpdateScope(long projectId, string scope, UpdateReviewGateScopeRequest request, CancellationToken ct) => Ok(await service.UpdateScope(projectId, scope, request, ct));
    [HttpGet("api/v1/departments/review-gates/matrix")]
    public async Task<ActionResult<IReadOnlyList<ProjectReviewMatrixRowDto>>> Matrix([FromQuery] string? mode, CancellationToken ct) => Ok(await service.Matrix(mode, null, ct));
    [HttpGet("api/v1/departments/{departmentId:long}/review-gates/matrix")]
    public async Task<ActionResult<IReadOnlyList<ProjectReviewMatrixRowDto>>> DepartmentMatrix(long departmentId, [FromQuery] string? mode, CancellationToken ct) => Ok(await service.Matrix(mode, departmentId, ct));
    [HttpGet("api/v1/projects/{projectId:long}/defense-readiness")]
    public async Task<ActionResult<DefenseReadinessDto>> DefenseReadiness(long projectId, CancellationToken ct) => Ok(await service.DefenseReadiness(projectId, ct));
    [HttpGet("api/v1/projects/{projectKey}/defense-readiness")]
    public async Task<ActionResult<DefenseReadinessDto>> DefenseReadinessByKey(string projectKey, CancellationToken ct) => Ok(await service.DefenseReadinessByKey(projectKey, ct));
    [HttpGet("api/v1/projects/{projectId:long}/review-gates/{gateCode}/requirements/{requirementId:long}/evidence/{version:int}/download")]
    public async Task<IActionResult> DownloadEvidence(long projectId, string gateCode, long requirementId, int version, CancellationToken ct)
    {
        var file = await service.DownloadEvidence(projectId, gateCode, requirementId, version, ct);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.CacheControl = "no-store";
        return File(file.Content, file.ContentType, file.FileName);
    }
}

public sealed class ReviewGateFileForm
{
    [Required] public IFormFile File { get; init; } = null!;
    [Required] public string RequirementId { get; init; } = "";
    public string? Notes { get; init; }
    public int ExpectedVersion { get; init; }
    public string? ConcurrencyToken { get; init; }
}
