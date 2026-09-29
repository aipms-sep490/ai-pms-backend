namespace AIPMS.Application.Features.Evaluations.DTOs;

public sealed record EligibleEvaluatorDto(long UserId, string DisplayName, long DepartmentId,
    string DepartmentName, IReadOnlyList<string> EvaluationTypes);
