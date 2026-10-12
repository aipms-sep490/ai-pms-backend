using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Results.Abstractions;
using AIPMS.Application.Features.Results.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController, Authorize]
[Route("api/v1/projects/{projectId:long}")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(404)]
public sealed class ResultHistoryController(IResultHistoryService history) : ControllerBase
{
    /// <summary>Published project versions, newest first; pageSize is limited to 100. No draft amendments.</summary>
    [HttpGet("result/history")]
    public Task<PagedResult<ResultRevisionSummary>> ProjectHistory(long projectId, CancellationToken ct, int page = 1, int pageSize = 20) =>
        history.History(projectId, null, page, pageSize, ct);
    /// <summary>Immutable published project version; version 1 remains the original publication.</summary>
    [HttpGet("result/versions/{version:int:min(1)}")]
    public Task<ProjectResultDto> ProjectVersion(long projectId, int version, CancellationToken ct) => history.ProjectVersion(projectId, version, ct);
    /// <summary>Own or academically scoped published student history; raw inputs and correction notes are redacted.</summary>
    [HttpGet("students/{studentId:long}/result/history")]
    public Task<PagedResult<ResultRevisionSummary>> StudentHistory(long projectId, long studentId, CancellationToken ct, int page = 1, int pageSize = 20) =>
        history.History(projectId, studentId, page, pageSize, ct);
    [HttpGet("students/{studentId:long}/result/versions/{version:int:min(1)}")]
    public Task<StudentResultDto> StudentVersion(long projectId, long studentId, int version, CancellationToken ct) =>
        history.StudentVersion(projectId, studentId, version, ct);
}
