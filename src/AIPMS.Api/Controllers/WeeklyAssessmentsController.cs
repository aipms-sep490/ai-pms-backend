using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.WeeklyAssessments;
using AIPMS.Application.Features.WeeklyAssessments.Abstractions;
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
public sealed class WeeklyAssessmentsController(IWeeklyAssessmentService service) : ControllerBase
{
    [HttpGet("api/v1/projects/{projectId:long}/weekly-assessments/weeks")]
    public Task<WeeklyAssessmentCalendar> Weeks(long projectId, CancellationToken ct) => service.Calendar(projectId, ct);
    [HttpGet("api/v1/projects/{projectId:long}/weekly-assessments/rubrics")]
    public Task<PagedResult<RubricRecord>> Rubrics(long projectId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default) => service.AvailableRubrics(projectId, page, pageSize, ct);
    [HttpGet("api/v1/projects/{projectId:long}/weekly-assessments")]
    public Task<PagedResult<WeeklyAssessmentDto>> History(long projectId, [FromQuery] long? studentId, [FromQuery] int? weekNumber,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => service.History(projectId, studentId, weekNumber, page, pageSize, ct);
    [HttpPost("api/v1/projects/{projectId:long}/weekly-assessments/draft")]
    public Task<WeeklyAssessmentDto> Save(long projectId, SaveWeeklyAssessmentRequest request, CancellationToken ct) => service.Save(projectId, request, ct);
    [HttpGet("api/v1/weekly-assessments/{id:long}")]
    public Task<WeeklyAssessmentDto> Get(long id, CancellationToken ct) => service.Get(id, ct);
    [HttpPost("api/v1/weekly-assessments/{id:long}/submit")]
    public Task<WeeklyAssessmentDto> Submit(long id, SubmitWeeklyAssessmentRequest request, CancellationToken ct) => service.Submit(id, request.ConcurrencyToken, ct);
}
