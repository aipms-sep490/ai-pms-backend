using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Results.DTOs;
using AIPMS.Application.Features.Semesters.DTOs;
using AIPMS.Application.Features.Semesters.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController, Authorize]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(409)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PolicyEvaluationController(IPeriodPolicyService policies, IEvaluationSchemeService schemes) : ControllerBase
{
    [HttpGet("api/v1/project-periods/{id:long}/policy-versions")]
    public Task<IReadOnlyList<PeriodPolicyDto>> PolicyHistory(long id, CancellationToken ct) => policies.HistoryAsync(id, ct);
    [HttpGet("api/v1/project-periods/{id:long}/effective-policy")]
    public Task<PeriodPolicyDto> Effective(long id, [FromQuery] DateTimeOffset? asOf, CancellationToken ct) => policies.GetAsync(id, asOf, ct);
    [HttpPut("api/v1/project-periods/{id:long}/policy")]
    public Task<PeriodPolicyDto> Policy(long id, UpdatePeriodPolicyRequest input, CancellationToken ct) => policies.PutAsync(id, input, ct);
    [HttpGet("api/v1/evaluation-schemes")]
    public Task<IReadOnlyList<EvaluationSchemeDto>> Schemes([FromQuery] long projectId, CancellationToken ct) => schemes.ListAsync(projectId, ct);
    [HttpGet("api/v1/evaluation-schemes/{id:long}")]
    public Task<EvaluationSchemeDto> Scheme(long id, CancellationToken ct) => schemes.GetAsync(id, ct);
    [HttpPost("api/v1/evaluation-schemes")]
    public async Task<ActionResult<EvaluationSchemeDto>> Create(SaveEvaluationSchemeRequest input, CancellationToken ct)
    { var row = await schemes.SaveAsync(null, input, ct); return CreatedAtAction(nameof(Scheme), new { id = row.Id }, row); }
    [HttpPut("api/v1/evaluation-schemes/{id:long}")]
    public Task<EvaluationSchemeDto> Update(long id, SaveEvaluationSchemeRequest input, CancellationToken ct) => schemes.SaveAsync(id, input, ct);
    [HttpDelete("api/v1/evaluation-schemes/{id:long}")]
    public async Task<IActionResult> Delete(long id, [FromQuery] string concurrencyToken, CancellationToken ct)
    { await schemes.DeleteAsync(id, concurrencyToken, ct); return NoContent(); }
    [HttpPost("api/v1/evaluation-schemes/{id:long}/publish")]
    public Task<EvaluationSchemeDto> Publish(long id, SchemeTokenRequest input, CancellationToken ct) => schemes.PublishAsync(id, input.ConcurrencyToken, ct);
    [HttpPost("api/v1/evaluation-schemes/{id:long}/versions")]
    public Task<EvaluationSchemeDto> Version(long id, SchemeTokenRequest input, CancellationToken ct) => schemes.VersionAsync(id, input.ConcurrencyToken, ct);
    [HttpGet("api/v1/projects/{projectId:long}/students/{studentId:long}/result/preview")]
    public Task<ProjectResultPreviewDto> PreviewStudent(long projectId, long studentId, CancellationToken ct) => schemes.PreviewAsync(projectId, studentId, ct);
    [HttpPost("api/v1/projects/{projectId:long}/students/{studentId:long}/result")]
    public Task<StudentResultDto> PublishStudent(long projectId, long studentId, PublishProjectResultRequest input, CancellationToken ct) =>
        schemes.PublishStudentAsync(projectId, studentId, input.ConfirmationToken, ct);
    [HttpGet("api/v1/projects/{projectId:long}/students/{studentId:long}/result")]
    public Task<StudentResultDto> Student(long projectId, long studentId, CancellationToken ct) => schemes.GetStudentAsync(projectId, studentId, ct);
}
