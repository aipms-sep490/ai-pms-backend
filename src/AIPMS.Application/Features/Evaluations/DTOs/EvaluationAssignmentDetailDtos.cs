using System.Text.Json.Serialization;

namespace AIPMS.Application.Features.Evaluations.DTOs;

public sealed record EvaluationAssignmentDetailDto(
    EvaluationAssignmentDto Assignment,
    bool CanScore,
    bool LegacyReadOnly,
    string? DenialReason);

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
    string? DownloadUrl);

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
    IReadOnlyList<EvaluationAssignmentEvidenceItemDto> Items)
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
