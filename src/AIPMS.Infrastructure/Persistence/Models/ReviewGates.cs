namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class ReviewGateTemplate
{
    public long Id { get; set; }
    public long? ProjectPeriodId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Mode { get; set; } = "SINGLE_MAJOR";
    public string Major { get; set; } = "";
    public string Status { get; set; } = "DRAFT";
    public int Version { get; set; } = 1;
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<ReviewGateDefinition> Gates { get; set; } = [];
}

public sealed class ReviewGateDefinition
{
    public long Id { get; set; }
    public long TemplateId { get; set; }
    public string Code { get; set; } = "";
    public int Sequence { get; set; }
    public string Title { get; set; } = "";
    public string Objective { get; set; } = "";
    public bool Required { get; set; } = true;
    public bool BlockNextGate { get; set; } = true;
    public bool BlocksDefense { get; set; }
    public decimal? Weight { get; set; }
    public DateTime? WindowStart { get; set; }
    public DateTime? WindowEnd { get; set; }
    public string ReviewerPolicy { get; set; } = "ASSIGNED_REVIEWER";
    public string ReviewScope { get; set; } = "COMMON";
    public ICollection<ReviewGateRequirement> Requirements { get; set; } = [];
}

public sealed class ReviewGateRequirement
{
    public long Id { get; set; }
    public long GateDefinitionId { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Type { get; set; } = "DOCUMENT";
    public string Scope { get; set; } = "COMMON";
    public long? MajorId { get; set; }
    public long? TargetStudentId { get; set; }
    public string Verification { get; set; } = "HUMAN";
    public bool Required { get; set; } = true;
    public string TargetCardinality { get; set; } = "PROJECT";
    public string? DeliveryStage { get; set; }
    public bool FeedsCold { get; set; }
    public bool FeedsDefense { get; set; }
    public string? InputKind { get; set; }
    public long? TemplateDocumentId { get; set; }
    public string? CarryForwardFromCode { get; set; }
    public string? PolicyTag { get; set; }
    public string? RubricCriterionRef { get; set; }
}

public sealed class ProjectGateInstance
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public long TemplateId { get; set; }
    public int TemplateVersion { get; set; }
    public string Code { get; set; } = "";
    public int Sequence { get; set; }
    public string Title { get; set; } = "";
    public string Objective { get; set; } = "";
    public string Status { get; set; } = "PLANNED";
    public string DecisionSource { get; set; } = "REVIEW";
    public long? OverriddenBy { get; set; }
    public DateTime? OverriddenAt { get; set; }
    public string? OverrideReason { get; set; }
    public string ReviewEligibility { get; set; } = "BLOCKED";
    public bool Required { get; set; } = true;
    public bool BlockNextGate { get; set; } = true;
    public bool BlocksDefense { get; set; }
    public decimal? Weight { get; set; }
    public DateTime? WindowStart { get; set; }
    public DateTime? WindowEnd { get; set; }
    public DateTime? SubmissionDueAt { get; set; }
    public DateTime? ReviewWindowEndAt { get; set; }
    public DateTime? RevisionDueAt { get; set; }
    public string? OffsetRuleJson { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; }
    public AIPMS.Infrastructure.Persistence.Generated.Models.Project Project { get; set; } = null!;
    public ICollection<ProjectGateRequirement> Requirements { get; set; } = [];
    public ICollection<GateReviewerAssignment> Assignments { get; set; } = [];
}

public sealed class ProjectGateRequirement
{
    public long Id { get; set; }
    public long GateInstanceId { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string Type { get; set; } = "DOCUMENT";
    public string Scope { get; set; } = "COMMON";
    public long? MajorId { get; set; }
    public long? TargetStudentId { get; set; }
    public bool Required { get; set; } = true;
    public string VerificationStatus { get; set; } = "PENDING";
    public long? LatestEvidenceId { get; set; }
    public string? ReviewerFeedback { get; set; }
    public string TargetCardinality { get; set; } = "PROJECT";
    public string? DeliveryStage { get; set; }
    public bool FeedsCold { get; set; }
    public bool FeedsDefense { get; set; }
    public string? InputKind { get; set; }
    public long? TemplateDocumentId { get; set; }
    public string? CarryForwardFromCode { get; set; }
    public string? PolicyTag { get; set; }
    public string? RubricCriterionRef { get; set; }
    public ProjectGateInstance GateInstance { get; set; } = null!;
    public ICollection<GateEvidence> Evidence { get; set; } = [];
}

public sealed class GateEvidence
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public long RequirementId { get; set; }
    public int Version { get; set; }
    public string ArtifactName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public string StorageKey { get; set; } = "";
    public string? LinkUrl { get; set; }
    public string? Notes { get; set; }
    public long SubmittedBy { get; set; }
    public DateTime SubmittedAt { get; set; }
    public string Status { get; set; } = "SUBMITTED";
    public string ScanStatus { get; set; } = "READY";
    public string? ScanProvider { get; set; }
    public DateTime? ScannedAt { get; set; }
    public string? ScanDetails { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public ProjectGateRequirement Requirement { get; set; } = null!;
}

public sealed class GateReviewerAssignment
{
    public long Id { get; set; }
    public long GateInstanceId { get; set; }
    public long ReviewerId { get; set; }
    public string Scope { get; set; } = "COMMON";
    public long? MajorId { get; set; }
    public long? StudentId { get; set; }
    public string Status { get; set; } = "PENDING_REVIEW";
    public string SessionRole { get; set; } = "MEMBER";
    public string? LatestDecision { get; set; }
    public string? ReviewerNotes { get; set; }
    public DateTime? RevokedAt { get; set; }
    public long? RevokedBy { get; set; }
    public string? RevocationReason { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; }
    public ProjectGateInstance GateInstance { get; set; } = null!;
}

public sealed class GateEvidenceVerification
{
    public long Id { get; set; }
    public long EvidenceId { get; set; }
    public long AssignmentId { get; set; }
    public long? PackageId { get; set; }
    public long? RequirementId { get; set; }
    public string Decision { get; set; } = "ACCEPTED";
    public string? Feedback { get; set; }
    public long ReviewerId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class GateDecision
{
    public long Id { get; set; }
    public long AssignmentId { get; set; }
    public long? PackageId { get; set; }
    public string Decision { get; set; } = "PASSED";
    public string Notes { get; set; } = "";
    public long DecidedBy { get; set; }
    public DateTime DecidedAt { get; set; }
}

public sealed class ReviewGatePackage
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public long GateInstanceId { get; set; }
    public int RoundNumber { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string? Notes { get; set; }
    public long SubmittedBy { get; set; }
    public DateTime SubmittedAt { get; set; }
    public ICollection<ReviewGatePackageItem> Items { get; set; } = [];
}

public sealed class ReviewGatePackageItem
{
    public long Id { get; set; }
    public long PackageId { get; set; }
    public long RequirementId { get; set; }
    public long EvidenceId { get; set; }
    public int EvidenceVersion { get; set; }
    public string Sha256 { get; set; } = "";
}

public sealed class FilePreviewDerivative
{
    public long Id { get; set; }
    public long? FileId { get; set; }
    public long? GateEvidenceId { get; set; }
    public string DerivativeType { get; set; } = "OFFICE_TEXT";
    public string Status { get; set; } = "READY";
    public string? PayloadJson { get; set; }
    public string? ErrorReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class ProjectGateScopeDecision
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public string Scope { get; set; } = "COMMON";
    public long? MajorId { get; set; }
    public string Status { get; set; } = "IN_REVIEW";
    public string? Notes { get; set; }
    public long DecidedBy { get; set; }
    public DateTime DecidedAt { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}
