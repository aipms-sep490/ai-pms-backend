using AIPMS.Application.Features.ProjectHub;
using AIPMS.Application.Features.ProjectHub.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController, Authorize]
[Route("api/v1/projects/{projectId:long}/hub")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(409)]
[ProducesResponseType<ProblemDetails>(422)]
public sealed class ProjectHubController(IProjectHubService hub) : ControllerBase
{
    [HttpPost("deliverables/{deliverableId:long}/versions")]
    [ProducesResponseType<SpecVersionDto>(201)]
    public async Task<ActionResult<SpecVersionDto>> CreateSpecVersion(
        long projectId, long deliverableId, CreateSpecVersionRequest input, CancellationToken ct) =>
        StatusCode(201, await hub.CreateSpecVersionAsync(projectId, deliverableId, input, ct));

    [HttpGet("deliverables/{deliverableId:long}/compare")]
    [ProducesResponseType<SpecVersionCompareDto>(200)]
    public Task<SpecVersionCompareDto> CompareSpecVersions(
        long projectId, long deliverableId, [FromQuery] int fromVersion, [FromQuery] int toVersion, CancellationToken ct) =>
        hub.CompareSpecVersionsAsync(projectId, deliverableId, fromVersion, toVersion, ct);

    [HttpGet("pinned-resources")]
    [ProducesResponseType<IReadOnlyList<PinnedResourceDto>>(200)]
    public Task<IReadOnlyList<PinnedResourceDto>> ListPinnedResources(long projectId, CancellationToken ct) =>
        hub.ListPinnedResourcesAsync(projectId, ct);

    [HttpPost("pinned-resources")]
    [ProducesResponseType<PinnedResourceDto>(201)]
    public async Task<ActionResult<PinnedResourceDto>> AddPinnedResource(
        long projectId, CreatePinnedResourceRequest input, CancellationToken ct) =>
        StatusCode(201, await hub.AddPinnedResourceAsync(projectId, input, ct));

    [HttpDelete("pinned-resources/{resourceId:long}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> DeletePinnedResource(long projectId, long resourceId, CancellationToken ct)
    {
        await hub.DeletePinnedResourceAsync(projectId, resourceId, ct);
        return NoContent();
    }

    [HttpGet("channels/{channelScope}")]
    [ProducesResponseType<HubChannelDto>(200)]
    public Task<HubChannelDto> GetChannel(long projectId, string channelScope, CancellationToken ct) =>
        hub.GetChannelAsync(projectId, channelScope, ct);

    [HttpPost("channels/{channelScope}/messages")]
    [ProducesResponseType<HubChannelMessageDto>(201)]
    public async Task<ActionResult<HubChannelMessageDto>> PostChannelMessage(
        long projectId, string channelScope, PostChannelMessageRequest input, CancellationToken ct) =>
        StatusCode(201, await hub.PostChannelMessageAsync(projectId, channelScope, input, ct));

    [HttpPost("chat/messages/{messageId:long}/to-task")]
    [ProducesResponseType<ChatConvertedTaskDto>(200)]
    public Task<ChatConvertedTaskDto> ConvertMessageToTask(
        long projectId, long messageId, ConvertMessageToTaskRequest input, CancellationToken ct) =>
        hub.ConvertMessageToTaskAsync(projectId, messageId, input, ct);

    [HttpPost("chat/messages/{messageId:long}/to-decision")]
    [ProducesResponseType<ChatConvertedDecisionDto>(200)]
    public Task<ChatConvertedDecisionDto> ConvertMessageToDecision(
        long projectId, long messageId, ConvertMessageToDecisionRequest input, CancellationToken ct) =>
        hub.ConvertMessageToDecisionAsync(projectId, messageId, input, ct);

    [HttpGet("gates/{gateCode}/anchored-comments")]
    [ProducesResponseType<IReadOnlyList<AnchoredCommentDto>>(200)]
    public Task<IReadOnlyList<AnchoredCommentDto>> ListAnchoredComments(long projectId, string gateCode, CancellationToken ct) =>
        hub.ListAnchoredCommentsAsync(projectId, gateCode, ct);

    [HttpPost("gates/{gateCode}/anchored-comments")]
    [ProducesResponseType<AnchoredCommentDto>(201)]
    public async Task<ActionResult<AnchoredCommentDto>> CreateAnchoredComment(
        long projectId, string gateCode, CreateAnchoredCommentRequest input, CancellationToken ct) =>
        StatusCode(201, await hub.CreateAnchoredCommentAsync(projectId, gateCode, input, ct));

    [HttpPost("gates/{gateCode}/anchored-comments/{commentId:long}/resolve")]
    [ProducesResponseType<AnchoredCommentDto>(200)]
    public Task<AnchoredCommentDto> ResolveAnchoredComment(long projectId, string gateCode, long commentId, CancellationToken ct) =>
        hub.ResolveAnchoredCommentAsync(projectId, gateCode, commentId, ct);

    [HttpGet("contribution-heatmap")]
    [ProducesResponseType<InternalContributionHeatmapDto>(200)]
    public Task<InternalContributionHeatmapDto> GetInternalHeatmap(long projectId, [FromQuery] int weeks = 4, CancellationToken ct = default) =>
        hub.GetInternalHeatmapAsync(projectId, weeks, ct);

    [HttpPost("evidence-rubric-links")]
    [ProducesResponseType<EvidenceRubricLinkDto>(201)]
    public async Task<ActionResult<EvidenceRubricLinkDto>> LinkEvidenceToRubric(
        long projectId, LinkEvidenceToRubricRequest input, CancellationToken ct) =>
        StatusCode(201, await hub.LinkEvidenceToRubricAsync(projectId, input, ct));

    [HttpGet("evidence-rubric-links")]
    [ProducesResponseType<IReadOnlyList<EvidenceRubricLinkDto>>(200)]
    public Task<IReadOnlyList<EvidenceRubricLinkDto>> ListEvidenceRubricLinks(long projectId, CancellationToken ct) =>
        hub.ListEvidenceRubricLinksAsync(projectId, ct);

    [HttpGet("health-pulse")]
    [ProducesResponseType<ProjectHealthPulseDto>(200)]
    public Task<ProjectHealthPulseDto> GetHealthPulse(long projectId, CancellationToken ct) =>
        hub.GetHealthPulseAsync(projectId, ct);

    [HttpGet("audit-timeline")]
    [ProducesResponseType<IReadOnlyList<ProjectAuditTimelineEntryDto>>(200)]
    public Task<IReadOnlyList<ProjectAuditTimelineEntryDto>> GetAuditTimeline(long projectId, [FromQuery] int limit = 50, CancellationToken ct = default) =>
        hub.GetAuditTimelineAsync(projectId, limit, ct);
}
