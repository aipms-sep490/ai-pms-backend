using AIPMS.Application.Common.Models;

namespace AIPMS.Application.Features.AcademicKnowledge;

public sealed record KnowledgeChunkInput(string? Heading, int? PageNumber, string Content);
public sealed record CreateKnowledgeDocumentRequest(string Title, string DocumentType, long? DepartmentId, long? AcademicSemesterId, long? ProjectPeriodId, string Version, string Content, string? SourceFileName, DateTime? EffectiveFrom, DateTime? EffectiveTo, IReadOnlyList<KnowledgeChunkInput>? Chunks);
public sealed record CreateKnowledgeVersionRequest(string Version, string Content, string? SourceFileName, DateTime? EffectiveFrom, DateTime? EffectiveTo, IReadOnlyList<KnowledgeChunkInput>? Chunks);
public sealed record AcademicAssistantAskRequest(string Query, string Scope = "ACADEMIC", long? ProjectId = null,
    long? ConversationId = null, int? ExpectedRevision = null, string? PageContext = null, string? DraftKind = null,
    string? ToolMode = null, long? RubricId = null);
public sealed record AssistantRubricOption(long Id, string Title, int Version, bool AssignedToProject);
public sealed record DeliverableReviewRequest(long RubricId, long? ChecklistId = null, string Scope = "MAJOR_SPECIFIC");
public sealed record DeliverableReviewCriterionDto(long CriterionId, string CriterionName, string Status,
    IReadOnlyList<string> Findings, IReadOnlyList<string> MissingEvidence, IReadOnlyList<string> Suggestions);
public sealed record DeliverableReviewCitationDto(string Title, string Version, string Section);
public sealed record DeliverableChecklistOption(long Id, string Title, int Version);
public sealed record DeliverableReviewOptions(string Scope, IReadOnlyList<AssistantRubricOption> Rubrics, IReadOnlyList<DeliverableChecklistOption> Checklists);
public sealed record DeliverableReviewResponseDto(long RunId, long ProjectId, long DeliverableVersionId,
    string Status, string Readiness, int ReadinessPercent, string Summary,
    IReadOnlyList<DeliverableReviewCriterionDto> Criteria,
    IReadOnlyList<DeliverableReviewCitationDto> Citations, bool InsufficientEvidence,
    string? LimitationNote, string GenerationStatus);
public sealed record CreateAssistantConversationRequest(long? ProjectId = null, string Scope = "ACADEMIC");
public sealed record UpdateAssistantConversationRequest(string? Title = null, bool? IsPinned = null);
public sealed record SubmitAssistantFeedbackRequest(short Rating, string? Note = null);
public sealed record AssistantFeedbackDto(long RunId, short Rating, string? Note, DateTime FeedbackAt);
public sealed record AssistantProjectOptionDto(long Id, string Code, string Title, string Status, string RoleLabel);
public sealed record AssistantConversationDto(long Id, string Title, long? ProjectId, string Scope, int Revision, DateTime UpdatedAt, bool IsPinned = false, int TurnCount = 0);
public sealed record AssistantTurnDto(long Id, string Query, DateTime SentAt, AcademicAssistantResponseDto Response);
public sealed record AssistantConversationDetailDto(AssistantConversationDto Conversation, IReadOnlyList<AssistantTurnDto> Turns, bool HasMore);
public sealed record AssistantMeetingDraftDto(DateTimeOffset? StartAt, DateTimeOffset? EndAt,
    string? MeetingDeliveryMode, string? VideoChannel, string? Location, string? OnlineUrl, bool InviteAll);
public sealed record AssistantTaskDraftDto(DateTimeOffset? DueAt, string? Priority, string? MilestoneTitle,
    IReadOnlyList<string> AssigneeNames, string? PrimaryMajorName);
public sealed record AssistantReportDraftDto(string? PeriodStart, string? PeriodEnd, string? CompletedWork,
    string? InProgressWork, string? Blockers, string? Risks, string? NextActions, string? PlannedWork);

// Structured diagnostic report for clear visual rendering (what/where/how far/who)
public sealed record AssistantContextCoverageDto(
    string? ProjectTitle,
    string? ProjectStage,
    int TasksScanned,
    int TasksTotal,
    int DeliverablesCount,
    int ColdItemsCount,
    int ReviewGatesCount);

public sealed record AssistantDiagnosticItemDto(
    string Category,
    string Title,
    string Location,
    string Status,
    string ProgressText,
    string? IssueDetail = null,
    string? Owner = null,
    string? DueAt = null,
    string? Target = null,
    long? ResourceId = null,
    string? CitationId = null);

public sealed record AssistantStructuredReportDto(
    string SummaryTitle,
    AssistantContextCoverageDto Coverage,
    IReadOnlyList<AssistantDiagnosticItemDto> Items);

// Stream A: AI Project Auto-Setup Plan (Milestones + Deliverables/Criteria + Tasks + TaskCriteria + Dependencies)
public sealed record AssistantSetupMilestoneDto(
    string TempKey,
    long? ExistingMilestoneId,
    string Title,
    string? Description,
    string? StartDate,
    string? DueDate);

public sealed record AssistantSetupDeliverableDto(
    string TempKey,
    string Title,
    string? Description,
    string DeliverableType,
    string? MilestoneRef,
    string EvidenceScope,
    string? TargetMajorName,
    DateTimeOffset? DueAt,
    IReadOnlyList<string> AcceptanceCriteria);

public sealed record AssistantSetupTaskDto(
    string TempKey,
    string Title,
    string? Description,
    string Priority,
    string? MilestoneRef,
    DateTimeOffset? DueAt,
    IReadOnlyList<string> AssigneeNames,
    string? PrimaryMajorName,
    IReadOnlyList<string> AcceptanceCriteria,
    IReadOnlyList<string> DependsOnRefs);

public sealed record AssistantProjectSetupPlanDto(
    string Summary,
    IReadOnlyList<AssistantSetupMilestoneDto> Milestones,
    IReadOnlyList<AssistantSetupDeliverableDto> Deliverables,
    IReadOnlyList<AssistantSetupTaskDto> Tasks,
    IReadOnlyList<string> Warnings);

// Stream B: AI Rubric Criteria Auto-Setup Plan (Draft Rubric for Department Staff / Admin)
public sealed record AssistantRubricLevelDraftDto(
    int LevelOrder,
    string Label,
    string Descriptor,
    decimal? ScoreFrom,
    decimal? ScoreTo);

public sealed record AssistantRubricCriterionDraftDto(
    string TempKey,
    string? ParentTempKey,
    string? CriterionCode,
    string Name,
    string? Description,
    decimal WeightPercent,
    decimal? MaxScore,
    bool IsRequired,
    string? LearningOutcome,
    string? EvidenceGuidance,
    IReadOnlyList<AssistantRubricLevelDraftDto> Levels);

public sealed record AssistantRubricSetupPlanDto(
    string Code,
    string Name,
    string? Description,
    string ProjectMode,
    string EvaluationStage,
    string EvaluationScope,
    string? MajorName,
    IReadOnlyList<AssistantRubricCriterionDraftDto> Criteria,
    IReadOnlyList<string> Warnings);

// Apply requests & results (One-Click Auto-Setup)
public sealed record ApplyProjectSetupRequest(
    Guid IdempotencyKey,
    long? RunId,
    IReadOnlyList<AssistantSetupMilestoneDto> Milestones,
    IReadOnlyList<AssistantSetupDeliverableDto> Deliverables,
    IReadOnlyList<AssistantSetupTaskDto> Tasks);

public sealed record CreatedSetupItemRef(string TempKey, long Id, string Title, string Kind);

public sealed record ApplyProjectSetupResultDto(
    long ProjectId,
    bool Replay,
    int MilestonesCreated,
    int DeliverablesCreated,
    int TasksCreated,
    int TaskCriteriaCreated,
    int DependenciesCreated,
    IReadOnlyList<CreatedSetupItemRef> Items,
    IReadOnlyList<string> Warnings);

public sealed record ApplyRubricSetupRequest(
    Guid IdempotencyKey,
    long? RunId,
    long? DepartmentId,
    long AcademicSemesterId,
    long? MajorId,
    string Code,
    string Name,
    string? Description,
    string ProjectMode,
    string EvaluationStage,
    string EvaluationScope,
    IReadOnlyList<AssistantRubricCriterionDraftDto> Criteria,
    DateOnly? EffectiveFrom = null,
    DateOnly? EffectiveTo = null);

public sealed record ApplyRubricSetupResultDto(
    long RubricId,
    string Code,
    string Name,
    string Status,
    int CriteriaCount,
    bool Replay,
    IReadOnlyList<string>? Warnings = null);

public sealed record AssistantDraftDto(string Kind, string Title, string Description, AssistantMeetingDraftDto? Meeting = null,
    AssistantTaskDraftDto? Task = null, AssistantReportDraftDto? Report = null, IReadOnlyList<AssistantDraftDto>? Tasks = null,
    AssistantProjectSetupPlanDto? SetupPlan = null, AssistantRubricSetupPlanDto? RubricSetup = null);
public sealed record AssistantStreamEvent(string Type, string? Text = null, AcademicAssistantResponseDto? Response = null);
public sealed record AssistantUpload(string FileName, byte[] Content);
public sealed record AssistantExport(string FileName, string ContentType, byte[] Content);
public sealed record KnowledgeCitationDto(long DocumentId, long VersionId, string Title, string Version, string DocumentType, string? Section, int? Page, DateTime? EffectiveFrom, string Excerpt);
public sealed record KnowledgeDocumentDto(long Id, string Title, string DocumentType, long? DepartmentId, long? AcademicSemesterId, long? ProjectPeriodId, string Status, string CurrentVersion, DateTime CreatedAt, DateTime UpdatedAt);
public sealed record KnowledgeVersionDto(long Id, long DocumentId, string Version, string Status, string? SourceFileName, DateTime? EffectiveFrom, DateTime? EffectiveTo, DateTime CreatedAt, DateTime? PublishedAt, int ChunkCount);
public sealed record AssistantAttachmentDto(string FileName, string MediaType);
public sealed record AcademicAssistantResponseDto(string Answer, string AnswerType, IReadOnlyList<KnowledgeCitationDto> Citations, IReadOnlyList<AIPMS.Application.Features.AiAssistant.Models.EvidenceReferenceDto> ProjectEvidence, bool InsufficientEvidence, string? LimitationNote, string GenerationStatus, DateTime GeneratedAt, long? RunId = null, IReadOnlyList<AssistantAttachmentDto>? Attachments = null, long? ConversationId = null, int? Revision = null, AssistantDraftDto? Draft = null, AssistantStructuredReportDto? StructuredReport = null, IReadOnlyList<string>? FollowUpSuggestions = null, short? FeedbackRating = null);
