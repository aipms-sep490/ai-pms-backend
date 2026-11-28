namespace AIPMS.Application.Features.Evaluations.DTOs;

public sealed record UpsertIndividualProcessScoreRequest(
    decimal Score,
    string? Comments = null,
    string? MentorReferenceNotes = null,
    string? ConcurrencyToken = null);

public sealed record FinalizeIndividualProcessScoreRequest(string ConcurrencyToken);

public sealed record UpsertMentorReferenceNoteRequest(string MentorReferenceNotes, string? ConcurrencyToken = null);

public sealed record GateIndividualProcessScoreItemDto(
    long Id,
    long GateInstanceId,
    int GateNumber,
    long StudentId,
    long? SupervisorAssignmentId,
    decimal? Score,
    decimal GateWeightPercent,
    string? MentorReferenceNotes,
    string? Comments,
    string Status,
    string ConcurrencyToken,
    DateTime UpdatedAt,
    DateTime? FinalizedAt);

public sealed record StudentIndividualProcessSummaryDto(
    long StudentId,
    string StudentName,
    long? MajorId,
    decimal? CumulativeScore,
    bool IsComplete,
    IReadOnlyList<GateIndividualProcessScoreItemDto> GateScores);

public sealed record ProjectIndividualProcessScoresDto(
    long ProjectId,
    string ViewerRole,
    bool CanEdit,
    IReadOnlyList<GateProcessWeightInput> GateWeights,
    IReadOnlyList<StudentIndividualProcessSummaryDto> Students);
