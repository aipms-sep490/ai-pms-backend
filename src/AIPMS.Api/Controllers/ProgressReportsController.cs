using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.ProgressMeetings.Commands;
using AIPMS.Application.Features.ProgressMeetings.DTOs;
using AIPMS.Application.Features.ProgressMeetings.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;
[ApiController, Authorize, Route("api/v1")]
public sealed class ProgressReportsController(ISender sender) : ControllerBase
{
    [HttpPost("projects/{projectId:long}/progress-reports")]
    public async Task<ActionResult<ProgressReportDto>> Create(long projectId, CreateProgressReportPayload p, CancellationToken ct) => Ok(await sender.Send(new CreateProgressReportCommand(projectId, p.ReportPeriodId, p.Summary, p.Completed, p.InProgress, p.Blockers, p.Risks, p.NextActions), ct));
    [HttpGet("projects/{projectId:long}/progress-reports")]
    public async Task<ActionResult<PagedResult<ProgressReportDto>>> List(long projectId, [FromQuery] string? reportType, [FromQuery] string? status, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => Ok(await sender.Send(new ListProgressReportsQuery(projectId, reportType, status, from, to, pageNumber, pageSize), ct));
    [HttpGet("progress-reports/{id:long}")] public async Task<ActionResult<ProgressReportDto>> Get(long id, CancellationToken ct) => Ok(await sender.Send(new GetProgressReportQuery(id), ct));
    [HttpPut("progress-reports/{id:long}")] public async Task<IActionResult> Update(long id, UpdateProgressReportPayload p, CancellationToken ct) { await sender.Send(new UpdateProgressReportCommand(id, p.Summary, p.Completed, p.InProgress, p.Blockers, p.Risks, p.NextActions), ct); return NoContent(); }
    [HttpPost("progress-reports/{id:long}/contributions")] public async Task<IActionResult> Contribute(long id, ContributionPayload p, CancellationToken ct) { await sender.Send(new AddProgressReportContributionCommand(id, p.SectionType, p.Content), ct); return NoContent(); }
    [HttpPost("progress-reports/{id:long}/submit")] public async Task<IActionResult> Submit(long id, CancellationToken ct) { await sender.Send(new SubmitProgressReportCommand(id), ct); return NoContent(); }
    [HttpPost("progress-reports/{id:long}/feedback")] public async Task<IActionResult> Feedback(long id, FeedbackPayload p, CancellationToken ct) { await sender.Send(new AddProgressReportFeedbackCommand(id, p.FeedbackText), ct); return NoContent(); }
}
public sealed record CreateProgressReportPayload(long ReportPeriodId, string Summary, string Completed, string InProgress, string Blockers, string Risks, string NextActions);
public sealed record UpdateProgressReportPayload(string Summary, string Completed, string InProgress, string Blockers, string Risks, string NextActions);
public sealed record ContributionPayload(string SectionType, string Content);
public sealed record FeedbackPayload(string FeedbackText);
