using AIPMS.Application.Features.Evaluations.DTOs;

namespace AIPMS.Application.Features.Results.DTOs;

public sealed record ScoreAmendmentInput(long EvaluationId, long CriterionId, decimal Score, string Reason,
    string? CriterionName = null);
public sealed record GateProcessAmendmentInput(long GateScoreId, int GateNumber, decimal Score, string Reason);
public sealed record DefenseRecoveredScoreInput(long ComponentId, decimal Score);
public sealed record DefenseAbsenceAmendmentInput(long AbsenceId, bool RevokeAbsence, string Reason,
    IReadOnlyList<DefenseRecoveredScoreInput>? RecoveredScores = null);
public sealed record ResultRevisionSummary(long? RevisionId, int Version, int? SupersedesVersion, long? StudentId,
    decimal TotalScore, string Outcome, long PublishedBy, DateTime PublishedAt, string? Reason, long? BundleId,
    string? PublishedByName = null, string? StudentName = null);
public sealed record RevisionCalculation(string FrozenInputs, decimal TotalScore, ResultBreakdownDto Breakdown,
    IReadOnlyList<ResultContributionDto> Contributions);
