using System.Text.Json.Serialization;

namespace AIPMS.Application.Features.Evaluations.DTOs;

public sealed record EvaluationAssignmentItemDto(
    long RubricCriterionId,
    string Name,
    string? Description,
    decimal WeightPercent,
    decimal EffectiveWeightPercent,
    decimal MaxScore,
    int SortOrder,
    bool IsRequired,
    string? CriterionCode,
    string? LearningOutcome,
    string? EvidenceGuidance,
    IReadOnlyList<RubricCriterionLevelDto> Levels,
    long? ParentId = null);

public sealed record EvaluationAssignmentDetailDto(
    EvaluationAssignmentDto Assignment,
    bool CanScore,
    bool LegacyReadOnly,
    string? DenialReason,
    string? Stage = null,
    decimal? ComponentStudentWeightPercent = null,
    string? SchemeApprovalStatus = null,
    bool? SchemeIsDemo = null,
    IReadOnlyList<EvaluationAssignmentItemDto>? Items = null,
    int? CompletedEvaluators = null,
    int? RequiredEvaluators = null)
{
    public IReadOnlyList<EvaluationAssignmentItemDto> Criteria => Items ?? [];
}

public sealed record EvaluationAssignmentEvidenceItemDto(
    long Id,
    string? Title,
    string? Description,
    string? SourceType,
    long? SourceId,
    long? FileId,
    string? FileName,
    string? ContentType,
    long? FileSizeBytes,
    long? MajorId,
    long? StudentId,
    DateTime? SubmittedAt,
    string? DownloadUrl,
    long? DeliverableVersionId = null,
    int? VersionNumber = null,
    string? Sha256 = null, long? ColdSubmissionId = null);

[method: JsonConstructor]
public sealed record EvaluationAssignmentEvidenceDto(
    long AssignmentId,
    long ProjectId,
    string Scope,
    long? MajorId,
    long? StudentId,
    long? FinalSubmissionId,
    DateTime? SubmittedAt,
    int ItemCount,
    bool IsReadOnly,
    IReadOnlyList<EvaluationAssignmentEvidenceItemDto> Items, string? Stage = null, long? ColdPackageId = null)
{
    public EvaluationAssignmentEvidenceDto(
        long assignmentId,
        long projectId,
        string scope,
        long? majorId,
        long? studentId,
        long? finalSubmissionId,
        DateTime? submittedAt,
        int itemCount,
        bool isReadOnly)
        : this(assignmentId, projectId, scope, majorId, studentId, finalSubmissionId, submittedAt, itemCount, isReadOnly, Array.Empty<EvaluationAssignmentEvidenceItemDto>())
    {
    }
}
