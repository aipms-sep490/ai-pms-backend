using AIPMS.Application.Common.Models;

namespace AIPMS.Application.Features.AcademicKnowledge.Abstractions;

public interface IAcademicKnowledgeService
{
    Task<PagedResult<KnowledgeDocumentDto>> ListAsync(long? departmentId, string? status, int page, int pageSize, CancellationToken ct);
    Task<KnowledgeDocumentDto> CreateAsync(CreateKnowledgeDocumentRequest request, CancellationToken ct);
    Task<KnowledgeVersionDto> AddVersionAsync(long documentId, CreateKnowledgeVersionRequest request, CancellationToken ct);
    Task<KnowledgeVersionDto> PublishAsync(long documentId, CancellationToken ct);
    Task<KnowledgeDocumentDto> RetireAsync(long documentId, CancellationToken ct);
    Task<IReadOnlyList<KnowledgeVersionDto>> HistoryAsync(long documentId, CancellationToken ct);
    Task<KnowledgeVersionDto> ReindexAsync(long documentId, CancellationToken ct);
    Task<AcademicAssistantResponseDto> AskAsync(AcademicAssistantAskRequest request, CancellationToken ct);
}
