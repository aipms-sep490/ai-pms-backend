using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Topics.Commands;
using AIPMS.Application.Features.Topics.DTOs;
using AIPMS.Application.Features.Topics.Models;
using AIPMS.Application.Features.Topics.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/topics")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TopicsController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<TopicDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<TopicDto>>> List([FromQuery] long? academicSemesterId,
        [FromQuery] long? projectPeriodId, [FromQuery] long? departmentId, [FromQuery] long? majorId,
        [FromQuery] string? projectMode, [FromQuery] string status = "PUBLISHED", [FromQuery] string? search = null,
        [FromQuery] bool compatibleOnly = false, [FromQuery] bool mineOnly = false,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await sender.Send(new ListTopicsQuery(new TopicFilter(academicSemesterId, projectPeriodId, departmentId,
            majorId, projectMode, status, search, compatibleOnly, mineOnly, page, pageSize)), ct));

    [HttpGet("similarity-check")]
    [ProducesResponseType<TopicSimilarityResultDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TopicSimilarityResultDto>> SimilarityCheck([FromQuery] string? title = null,
        [FromQuery] string? keywords = null, [FromQuery] long? leadDepartmentId = null,
        [FromQuery] int periodWindow = 3, CancellationToken ct = default) =>
        Ok(await sender.Send(new CheckTopicSimilarityQuery(title, keywords, leadDepartmentId, periodWindow), ct));

    [HttpPost("ai-similarity-check")]
    [Authorize(Roles = "STUDENT")]
    [AIPMS.Api.Filters.AiFeatureGate(AIPMS.Api.Filters.AiFeatureArea.TopicSimilarity)]
    [EnableRateLimiting("ai-assistant")]
    [ProducesResponseType<TopicAiSimilarityResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<TopicAiSimilarityResultDto>> AiSimilarityCheck(TopicAiSimilarityRequest request,
        CancellationToken ct) => Ok(await sender.Send(new CheckTopicAiSimilarityQuery(request), ct));

    [HttpGet("{id:long}")]
    [ProducesResponseType<TopicDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TopicDto>> Get(long id, CancellationToken ct) => Ok(await sender.Send(new GetTopicQuery(id), ct));

    [HttpPost]
    [ProducesResponseType<TopicDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<TopicDto>> Create(CreateTopicRequest request, CancellationToken ct)
    {
        var topic = await sender.Send(new CreateTopicCommand(request), ct);
        return CreatedAtAction(nameof(Get), new { id = topic.Id }, topic);
    }

    [HttpPut("{id:long}")]
    [ProducesResponseType<TopicDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TopicDto>> Update(long id, UpdateTopicRequest request, CancellationToken ct) =>
        Ok(await sender.Send(new UpdateTopicCommand(id, request), ct));

    [HttpPost("{id:long}/publish")]
    [ProducesResponseType<TopicDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TopicDto>> Publish(long id, PublishTopicRequest request, CancellationToken ct) =>
        Ok(await sender.Send(new PublishTopicCommand(id, request), ct));

    [HttpPost("{id:long}/close")]
    [ProducesResponseType<TopicDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TopicDto>> Close(long id, CloseTopicRequest request, CancellationToken ct) =>
        Ok(await sender.Send(new CloseTopicCommand(id, request), ct));
}
