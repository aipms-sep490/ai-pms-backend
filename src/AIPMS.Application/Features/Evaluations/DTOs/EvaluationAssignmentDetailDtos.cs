namespace AIPMS.Application.Features.Evaluations.DTOs;

public sealed record EvaluationAssignmentDetailDto(
    EvaluationAssignmentDto Assignment,
    bool CanScore,
    bool LegacyReadOnly,
    string? DenialReason);

public sealed record EvaluationAssignmentEvidenceDto(
    long AssignmentId,
    long ProjectId,
    string Scope,
    long? MajorId,
    long? StudentId,
    long? FinalSubmissionId,
    DateTime? SubmittedAt,
    int ItemCount,
    bool IsReadOnly);
