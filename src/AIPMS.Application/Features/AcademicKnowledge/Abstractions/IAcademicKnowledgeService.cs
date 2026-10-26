using AIPMS.Application.Common.Models;

namespace AIPMS.Application.Features.AcademicKnowledge.Abstractions;

public interface IAcademicKnowledgeService
{
    Task<IReadOnlyList<AssistantRubricOption>> ReviewRubricsAsync(long projectId, CancellationToken ct);
    Task<PagedResult<KnowledgeDocumentDto>> ListAsync(long? departmentId, string? status, int page, int pageSize, CancellationToken ct);
    Task<KnowledgeDocumentDto> CreateAsync(CreateKnowledgeDocumentRequest request, CancellationToken ct);
    Task<KnowledgeVersionDto> AddVersionAsync(long documentId, CreateKnowledgeVersionRequest request, CancellationToken ct);
    Task<KnowledgeVersionDto> PublishAsync(long documentId, CancellationToken ct);
    Task<KnowledgeDocumentDto> RetireAsync(long documentId, CancellationToken ct);
    Task<IReadOnlyList<KnowledgeVersionDto>> HistoryAsync(long documentId, CancellationToken ct);
    Task<KnowledgeVersionDto> ReindexAsync(long documentId, CancellationToken ct);
    Task<AcademicAssistantResponseDto> AskAsync(AcademicAssistantAskRequest request, CancellationToken ct);
    Task<AcademicAssistantResponseDto> AskWithFilesAsync(AcademicAssistantAskRequest request, IReadOnlyList<AssistantUpload> files, CancellationToken ct);
    Task<AssistantExport> ExportAsync(long runId, string format, CancellationToken ct);
    Task<AssistantConversationDto> CreateConversationAsync(CreateAssistantConversationRequest request, CancellationToken ct);
    Task<IReadOnlyList<AssistantConversationDto>> ConversationsAsync(long? projectId, long? beforeId, CancellationToken ct);
    Task<AssistantConversationDetailDto> ConversationAsync(long id, long? beforeTurnId, CancellationToken ct);
    Task DeleteConversationAsync(long id, CancellationToken ct);
    Task<KnowledgeCitationDto> CitationAsync(long runId, int index, CancellationToken ct);
    Task<AcademicAssistantResponseDto> AskStreamingAsync(AcademicAssistantAskRequest request, IReadOnlyList<AssistantUpload> files, Func<AssistantStreamEvent, Task> emit, CancellationToken ct);
}
