namespace AIPMS.Application.Features.Evaluations.DTOs;

public sealed record SchemeComponentInput(string Name, string Scope, long? MajorId, long RubricId,
    decimal ProjectWeightPercent, decimal StudentWeightPercent, int RequiredEvaluators);
public sealed record SaveEvaluationSchemeRequest(long ProjectId, long ProjectPeriodId, string Name,
    decimal PassThreshold, IReadOnlyList<SchemeComponentInput> Components, string? ConcurrencyToken = null);
public sealed record SchemeComponentDto(long Id, string Name, string Scope, long? MajorId, long RubricId,
    decimal ProjectWeightPercent, decimal StudentWeightPercent, int RequiredEvaluators);
public sealed record SchemeStudent(long StudentId, long MajorId, long DepartmentId);
public sealed record EvaluationSchemeDto(long Id, long RootId, int Version, long ProjectId, long ProjectPeriodId,
    string Name, string Status, decimal PassThreshold, string ConcurrencyToken, long? PolicyVersionId,
    IReadOnlyList<SchemeComponentDto> Components, IReadOnlyList<SchemeStudent> Students, string CalculationRule);
public sealed record SchemeTokenRequest(string ConcurrencyToken);
public sealed record ScopedAssignmentContext(long ComponentId, long SchemeId, string Scope, long? MajorId,
    long? StudentId, long RubricId, long PolicyVersionId, string SnapshotJson);
public sealed record StudentResultDto(long Id, long ProjectId, long StudentId, long MajorId, long SchemeId,
    decimal TotalScore, decimal PassThreshold, string Outcome, string CalculationRule,
    long PublishedBy, DateTime PublishedAt, string SnapshotJson);
