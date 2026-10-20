using AIPMS.Application.Common.Models;

namespace AIPMS.Application.Features.ReviewGates;

public sealed record ReviewGateRequirementInput(string Code, string Title, string? Description, string Type,
    string Scope, long? MajorId, long? TargetStudentId = null, string Verification = "HUMAN", bool Required = true);
public sealed record ReviewGateDefinitionInput(string Code, int Sequence, string Title, string Objective,
    bool Required, bool BlockNextGate, bool BlocksDefense, DateTime? Start, DateTime? End,
    string ReviewerPolicy, string ReviewScope, IReadOnlyList<ReviewGateRequirementInput> Requirements);
public sealed record SaveReviewGateTemplateRequest(long? ProjectPeriodId, string Code, string Name, string Mode,
    string Major, string Version, string? ConcurrencyToken, IReadOnlyList<ReviewGateDefinitionInput> Gates);
public sealed record PublishReviewGateTemplateRequest(string? ConcurrencyToken);
public sealed record ReviewGateTemplateDto(string Id, long? ProjectPeriodId, string Code, string Name, string Mode,
    string Major, string Status, string Version, string ConcurrencyToken, int GatesCount,
    IReadOnlyList<ReviewGateDefinitionDto> Gates);
public sealed record ReviewGateDefinitionDto(string Id, string Code, int Sequence, string Title, string Objective,
    bool Required, bool BlockNextGate, bool BlocksDefense, DateTime? WindowStart, DateTime? WindowEnd,
    DateTime? Start, DateTime? End, string ReviewerPolicy, string ReviewScope, IReadOnlyList<ReviewGateRequirementDto> Requirements);
public sealed record ReviewGateRequirementDto(string Id, string Code, string Title, string? Description, string Type,
    string Scope, long? MajorId, long? TargetStudentId, string Verification, bool Required);

public sealed record StudentReviewTimelineDto(string ProjectId, string ProjectCode, string ProjectName, string ProjectMode,
    string Major, string EffectivePolicyVersion, string CurrentGateCode, IReadOnlyList<StudentGateInstanceDto> Gates,
    ReviewGateCapabilitiesDto Capabilities);
public sealed record StudentGateInstanceDto(string GateInstanceId, string Code, string Title, string Objective, string Status,
    string ReviewEligibility, bool IsRequired, DateTime? WindowStart, DateTime? WindowEnd,
    IReadOnlyList<ReviewGateBlockingReasonDto> BlockingReasons, IReadOnlyList<StudentGateRequirementDto> Requirements,
    string ConcurrencyToken = "");
public sealed record ReviewGateBlockingReasonDto(string Code, string? RequirementCode, string Message);
public sealed record ReviewGateCapabilitiesDto(bool CanSubmitPackage, bool CanSubmitRevision, IReadOnlyList<string> BlockingReasons);
public sealed record StudentGateRequirementDto(string Id, string Code, string Title, string Type, string Scope, bool IsMandatory,
    string VerificationStatus, ReviewGateArtifactDto? LatestArtifact, string? ReviewerFeedback);
public sealed record ReviewGateArtifactDto(int Version, string Filename, DateTime SubmittedAt, string SubmittedBy, string? Notes,
    string ContentType = "application/octet-stream", long SizeBytes = 0, string Sha256 = "", string DownloadUrl = "");
public sealed record SubmitReviewGateEvidenceRequest(string RequirementId, string ArtifactName, string? Notes, int ExpectedVersion,
    string? ConcurrencyToken);
public sealed record SubmitReviewGateFileRequest(string RequirementId, string? Notes, int ExpectedVersion, string? ConcurrencyToken);
public sealed record SubmitReviewGateForReviewRequest(string? Notes, string? ConcurrencyToken);

public sealed record AssignedReviewItemDto(string Id, string ProjectId, string ProjectCode, string ProjectName, string GateCode,
    string GateTitle, string Scope, DateTime? DueAt, string Status, string? LatestDecision,
    IReadOnlyList<StudentGateRequirementDto> Requirements, string? ReviewerNotes, string ConcurrencyToken,
    int Page = 1, int PageSize = 100, long TotalCount = 0);
public sealed record VerifyReviewGateEvidenceRequest(string RequirementId, string Decision, string? Feedback, string ConcurrencyToken);
public sealed record SubmitReviewGateDecisionRequest(string Decision, string Notes, string ConcurrencyToken);
public sealed record UpdateReviewGateDecisionRequest(string NewStatus, string? ConcurrencyToken);
public sealed record UpdateReviewGateScopeRequest(string NewStatus, string? ConcurrencyToken);
public sealed record CreateReviewGateAssignmentRequest(string ProjectId, string GateCode, long ReviewerId,
    string Scope, long? MajorId, long? StudentId, string? ConcurrencyToken);
public sealed record RevokeReviewGateAssignmentRequest(string ConcurrencyToken, string Reason);

public sealed record ProjectReviewMatrixRowDto(string ProjectId, string ProjectName, string Mode, string Major, string Dept,
    decimal TaskProgress, IReadOnlyDictionary<string, string> GateStatuses,
    IReadOnlyDictionary<string, IReadOnlyList<string>> BlockingReasons,
    IReadOnlyDictionary<string, string>? HybridScopes);
public sealed record DefenseReadinessDto(string ProjectId, string ProjectName, bool IsReady, int BlockedCount,
    IReadOnlyList<DefenseConditionDto> Conditions);
public sealed record DefenseConditionDto(string Id, string Title, bool? Satisfied, string Detail, bool IsMandatory);

