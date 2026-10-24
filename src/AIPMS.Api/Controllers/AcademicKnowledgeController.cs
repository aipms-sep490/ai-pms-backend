using AIPMS.Application.Common.Models;
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
public sealed class AcademicKnowledgeController(IAcademicKnowledgeService service) : ControllerBase
{
    [HttpPost("assistant/ask")]
    [EnableRateLimiting("ai-assistant")]
    [ProducesResponseType<AcademicAssistantResponseDto>(200)]
    public Task<AcademicAssistantResponseDto> Ask(AcademicAssistantAskRequest request, CancellationToken ct) => service.AskAsync(request, ct);

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
