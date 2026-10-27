namespace AIPMS.Application.Features.Evaluations.DTOs;

public sealed record SchemeComponentInput(string Name, string Scope, long? MajorId, long RubricId,
    decimal ProjectWeightPercent, decimal StudentWeightPercent, int RequiredEvaluators,
    IReadOnlyList<SchemeEvaluatorWeight>? EvaluatorWeights = null, string Stage = "DEFENSE");
public sealed record SchemeEvaluatorWeight(long EvaluatorId, decimal WeightPercent);
public sealed record SchemePolicyInput(bool IsDemo = true, string ApprovalStatus = "DEMO",
    string? ApprovalReference = null, string Formula = "COMPONENT_WEIGHTS",
    string CalculationVersion = "DECIMAL_V2", string RoundingMode = "AWAY_FROM_ZERO", int DecimalPlaces = 2);
public sealed record SaveEvaluationSchemeRequest(long ProjectId, long ProjectPeriodId, string Name,
    decimal PassThreshold, IReadOnlyList<SchemeComponentInput> Components, string? ConcurrencyToken = null,
    SchemePolicyInput? Policy = null);
public sealed record SchemeComponentDto(long Id, string Name, string Scope, long? MajorId, long RubricId,
    decimal ProjectWeightPercent, decimal StudentWeightPercent, int RequiredEvaluators,
    IReadOnlyList<SchemeEvaluatorWeight>? EvaluatorWeights = null, string? Stage = null);
public sealed record SchemeStudent(long StudentId, long MajorId, long DepartmentId);
public sealed record EvaluationSchemeDto(long Id, long RootId, int Version, long ProjectId, long ProjectPeriodId,
    string Name, string Status, decimal PassThreshold, string ConcurrencyToken, long? PolicyVersionId,
    IReadOnlyList<SchemeComponentDto> Components, IReadOnlyList<SchemeStudent> Students, string CalculationRule,
    SchemePolicyInput? Policy = null, DateTime? LockedAt = null);
public sealed record SchemeTokenRequest(string ConcurrencyToken);
public sealed record UpdateSchemeComponentRequest(string ConcurrencyToken, SchemeComponentInput Component);
public sealed record ScopedAssignmentContext(long ComponentId, long SchemeId, string Scope, long? MajorId,
    long? StudentId, long RubricId, long PolicyVersionId, string SnapshotJson, string? Stage = null, long? ColdPackageId = null);
public sealed record StudentResultDto(long Id, long ProjectId, long StudentId, long MajorId, long SchemeId,
    decimal TotalScore, decimal PassThreshold, string Outcome, string CalculationRule,
    long PublishedBy, DateTime PublishedAt, string SnapshotJson,
    ResultBreakdownDto? Breakdown = null, int Version = 1, long? RevisionId = null, int? SupersedesVersion = null);

public sealed record ResultComponentScoreDto(long ComponentId, string Name, string Scope, long? MajorId,
    decimal WeightPercent, decimal? Score, int RequiredEvaluators, int FinalizedEvaluators, string? Stage = null);
public sealed record ResultMajorScoreDto(long MajorId, decimal? Score);
public sealed record EvaluationFormulaComponentDto(string Type, decimal WeightPercent);
public sealed record EvaluationFormulaDto(IReadOnlyList<EvaluationFormulaComponentDto> Components, string? Description = null);

public sealed record ResultBreakdownDto(string? SchemeName, int? SchemeVersion, string CalculationVersion,
    bool IsLegacy, bool? IsDemo, string ApprovalStatus, string Readiness, decimal? UnroundedTotal,
    decimal? CommonScore, decimal? MajorAggregate, IReadOnlyList<ResultMajorScoreDto> MajorBreakdown,
    IReadOnlyList<ResultComponentScoreDto> Components,
    EvaluationFormulaDto? Formula = null);
