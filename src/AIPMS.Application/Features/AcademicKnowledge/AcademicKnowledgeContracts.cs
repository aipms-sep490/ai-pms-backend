using AIPMS.Application.Common.Models;

namespace AIPMS.Application.Features.AcademicKnowledge;

public sealed record KnowledgeChunkInput(string? Heading, int? PageNumber, string Content);
public sealed record CreateKnowledgeDocumentRequest(string Title, string DocumentType, long? DepartmentId, long? AcademicSemesterId, long? ProjectPeriodId, string Version, string Content, string? SourceFileName, DateTime? EffectiveFrom, DateTime? EffectiveTo, IReadOnlyList<KnowledgeChunkInput>? Chunks);
public sealed record CreateKnowledgeVersionRequest(string Version, string Content, string? SourceFileName, DateTime? EffectiveFrom, DateTime? EffectiveTo, IReadOnlyList<KnowledgeChunkInput>? Chunks);
public sealed record AcademicAssistantAskRequest(string Query, string Scope = "ACADEMIC", long? ProjectId = null,
    long? ConversationId = null, int? ExpectedRevision = null, string? PageContext = null, string? DraftKind = null);
public sealed record CreateAssistantConversationRequest(long? ProjectId = null, string Scope = "ACADEMIC");
public sealed record AssistantConversationDto(long Id, string Title, long? ProjectId, string Scope, int Revision, DateTime UpdatedAt);
public sealed record AssistantTurnDto(long Id, string Query, AcademicAssistantResponseDto Response);
public sealed record AssistantConversationDetailDto(AssistantConversationDto Conversation, IReadOnlyList<AssistantTurnDto> Turns, bool HasMore);
public sealed record AssistantDraftDto(string Kind, string Title, string Description);
public sealed record AssistantStreamEvent(string Type, string? Text = null, AcademicAssistantResponseDto? Response = null);
public sealed record AssistantUpload(string FileName, byte[] Content);
public sealed record AssistantExport(string FileName, string ContentType, byte[] Content);
public sealed record KnowledgeCitationDto(long DocumentId, long VersionId, string Title, string Version, string DocumentType, string? Section, int? Page, DateTime? EffectiveFrom, string Excerpt);
public sealed record KnowledgeDocumentDto(long Id, string Title, string DocumentType, long? DepartmentId, long? AcademicSemesterId, long? ProjectPeriodId, string Status, string CurrentVersion, DateTime CreatedAt, DateTime UpdatedAt);
public sealed record KnowledgeVersionDto(long Id, long DocumentId, string Version, string Status, string? SourceFileName, DateTime? EffectiveFrom, DateTime? EffectiveTo, DateTime CreatedAt, DateTime? PublishedAt, int ChunkCount);
public sealed record AssistantAttachmentDto(string FileName, string MediaType);
public sealed record AcademicAssistantResponseDto(string Answer, string AnswerType, IReadOnlyList<KnowledgeCitationDto> Citations, IReadOnlyList<AIPMS.Application.Features.AiAssistant.Models.EvidenceReferenceDto> ProjectEvidence, bool InsufficientEvidence, string? LimitationNote, string GenerationStatus, DateTime GeneratedAt, long? RunId = null, IReadOnlyList<AssistantAttachmentDto>? Attachments = null, long? ConversationId = null, int? Revision = null, AssistantDraftDto? Draft = null);
