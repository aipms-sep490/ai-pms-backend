using AIPMS.Application.Common.Models;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.AcademicKnowledge;
using AIPMS.Application.Features.AcademicKnowledge.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AIPMS.Api.Controllers;

[ApiController]
[Route("api/v1/academic")]
[Authorize]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class AcademicKnowledgeController(IAcademicKnowledgeService service, ILogger<AcademicKnowledgeController> logger, IRequestContext requestContext) : ControllerBase
{
    [HttpPost("assistant/ask")]
    [EnableRateLimiting("ai-assistant")]
    [ProducesResponseType<AcademicAssistantResponseDto>(200)]
    public Task<AcademicAssistantResponseDto> Ask(AcademicAssistantAskRequest request, CancellationToken ct) => service.AskAsync(request, ct);

    [HttpPost("assistant/ask-with-files")]
    [EnableRateLimiting("ai-assistant")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(9 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 9 * 1024 * 1024)]
    [ProducesResponseType<AcademicAssistantResponseDto>(200)]
    public async Task<AcademicAssistantResponseDto> AskWithFiles([FromForm] AssistantFilesForm input, CancellationToken ct)
    {
        if (input.Files.Count > 4 || input.Files.Sum(f => f.Length) > 8 * 1024 * 1024 || input.Files.Any(f => f.Length is 0 or > 4 * 1024 * 1024))
            throw new ArgumentException("Tối đa 4 tệp, mỗi tệp 4 MB và tổng 8 MB.");
        var files = new List<AssistantUpload>();
        foreach (var file in input.Files)
        {
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream, ct);
            files.Add(new(file.FileName, stream.ToArray()));
        }
        return await service.AskWithFilesAsync(input.ToRequest(), files, ct);
    }

    [HttpPost("assistant/conversations")]
    [EnableRateLimiting("ai-assistant")]
    public Task<AssistantConversationDto> CreateConversation(CreateAssistantConversationRequest request, CancellationToken ct) => service.CreateConversationAsync(request, ct);

    [HttpGet("assistant/conversations")]
    public Task<IReadOnlyList<AssistantConversationDto>> Conversations([FromQuery] long? projectId, [FromQuery] long? beforeId, CancellationToken ct) => service.ConversationsAsync(projectId, beforeId, ct);

    [HttpGet("assistant/conversations/{id:long}")]
    public Task<AssistantConversationDetailDto> Conversation(long id, [FromQuery] long? beforeTurnId, CancellationToken ct) => service.ConversationAsync(id, beforeTurnId, ct);

    [HttpDelete("assistant/conversations/{id:long}")]
    public async Task<IActionResult> DeleteConversation(long id, CancellationToken ct) { await service.DeleteConversationAsync(id, ct); return NoContent(); }

    [HttpGet("assistant/runs/{id:long}/citations/{index:int}")]
    public async Task<KnowledgeCitationDto> Citation(long id, int index, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return await service.CitationAsync(id, index, ct);
    }

    [HttpPost("assistant/stream")]
    [EnableRateLimiting("ai-assistant")]
    [Consumes("multipart/form-data")]
    [Produces("application/x-ndjson")]
    [RequestSizeLimit(9 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 9 * 1024 * 1024)]
    public async Task Stream([FromForm] AssistantFilesForm input, CancellationToken ct)
    {
        if (input.Files.Count > 4 || input.Files.Sum(f => f.Length) > 8 * 1024 * 1024 || input.Files.Any(f => f.Length is 0 or > 4 * 1024 * 1024))
            throw new ArgumentException("Tối đa 4 tệp, mỗi tệp 4 MB và tổng 8 MB.");
        var files = new List<AssistantUpload>();
        foreach (var file in input.Files)
        {
            using var buffer = new MemoryStream(); await file.CopyToAsync(buffer, ct);
            files.Add(new(file.FileName, buffer.ToArray()));
        }
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Accel-Buffering"] = "no";
        // Audit uses this after the response begins, so allocate its header before streaming.
        _ = requestContext.CorrelationId;
        async Task Emit(AssistantStreamEvent item)
        {
            if (!Response.HasStarted) Response.ContentType = "application/x-ndjson; charset=utf-8";
            await Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(item, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)) + "\n", ct);
            await Response.Body.FlushAsync(ct);
        }
        try
        {
            var result = await service.AskStreamingAsync(input.ToRequest(), files, Emit, ct);
            await Emit(new("completed", Response: result));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) when (Response.HasStarted)
        {
            logger.LogWarning("Assistant stream could not complete: {FailureType}; {FailureLocation}", ex.GetType().Name, ex.StackTrace);
            await Emit(new("error", "Chưa lưu được câu trả lời. Mở lại lịch sử để kiểm tra trước khi gửi lại."));
        }
    }

    [HttpGet("assistant/runs/{id:long}/download")]
    [EnableRateLimiting("ai-assistant")]
    public async Task<IActionResult> Download(long id, [FromQuery] string format = "md", CancellationToken ct = default)
    {
        var file = await service.ExportAsync(id, format, ct);
        Response.Headers.CacheControl = "no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpGet("knowledge-documents")]
    [Authorize(Policy = AuthorizationPolicies.AcademicManagement)]
    public Task<PagedResult<KnowledgeDocumentDto>> List([FromQuery] long? departmentId, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => service.ListAsync(departmentId, status, page, pageSize, ct);

    [HttpPost("knowledge-documents")]
    [Authorize(Policy = AuthorizationPolicies.AcademicManagement)]
    public async Task<ActionResult<KnowledgeDocumentDto>> Create(CreateKnowledgeDocumentRequest request, CancellationToken ct) => CreatedAtAction(nameof(List), await service.CreateAsync(request, ct));

    [HttpPost("knowledge-documents/{id:long}/versions")]
    [Authorize(Policy = AuthorizationPolicies.AcademicManagement)]
    public Task<KnowledgeVersionDto> AddVersion(long id, CreateKnowledgeVersionRequest request, CancellationToken ct) => service.AddVersionAsync(id, request, ct);

    [HttpPost("knowledge-documents/{id:long}/publish")]
    [Authorize(Policy = AuthorizationPolicies.AcademicManagement)]
    public Task<KnowledgeVersionDto> Publish(long id, CancellationToken ct) => service.PublishAsync(id, ct);

    [HttpPost("knowledge-documents/{id:long}/retire")]
    [Authorize(Policy = AuthorizationPolicies.AcademicManagement)]
    public Task<KnowledgeDocumentDto> Retire(long id, CancellationToken ct) => service.RetireAsync(id, ct);

    [HttpGet("knowledge-documents/{id:long}/history")]
    [Authorize(Policy = AuthorizationPolicies.AcademicManagement)]
    public Task<IReadOnlyList<KnowledgeVersionDto>> History(long id, CancellationToken ct) => service.HistoryAsync(id, ct);

    [HttpPost("knowledge-documents/{id:long}/reindex")]
    [Authorize(Policy = AuthorizationPolicies.AcademicManagement)]
    public Task<KnowledgeVersionDto> Reindex(long id, CancellationToken ct) => service.ReindexAsync(id, ct);
}

public sealed class AssistantFilesForm
{
    public string Query { get; set; } = "";
    public string Scope { get; set; } = "ACADEMIC";
    public long? ProjectId { get; set; }
    public long? ConversationId { get; set; }
    public int? ExpectedRevision { get; set; }
    public string? PageContext { get; set; }
    public string? DraftKind { get; set; }
    public List<IFormFile> Files { get; set; } = [];
    public AcademicAssistantAskRequest ToRequest() => new(Query, Scope, ProjectId, ConversationId, ExpectedRevision, PageContext, DraftKind);
}
