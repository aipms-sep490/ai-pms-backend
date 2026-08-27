using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Models;

namespace AIPMS.Infrastructure.Persistence.Mappers;

internal static class EvaluationMapper
{
    internal static RubricDto ToDto(this Rubric rubric, RubricVersionMetadata? metadata) => new(
        rubric.Id, rubric.DepartmentId, rubric.AcademicSemesterId, rubric.Code, rubric.Name,
        rubric.Description, metadata?.VersionNumber ?? 1, metadata?.ApprovalStatus ?? "DRAFT",
        rubric.IsActive, rubric.CreatedBy, rubric.CreatedAt, rubric.UpdatedAt,
        metadata?.ApprovedBy, metadata?.ApprovedAt,
        rubric.RubricCriteria.OrderBy(x => x.SortOrder).ThenBy(x => x.Id).Select(x =>
            new RubricCriterionDto(x.Id, x.CriterionId, x.Criterion.Code, x.Criterion.Name,
                x.Criterion.Description, x.WeightPercent, x.MaxScore, x.SortOrder, x.IsRequired)).ToList());

    internal static EvaluationDto ToDto(this Evaluation evaluation,
        RubricVersionMetadata? rubricMetadata, EvaluationAudit? audit) => new(
        evaluation.Id, evaluation.ProjectId, evaluation.EvaluatorId, evaluation.RubricId,
        rubricMetadata?.VersionNumber ?? 1, evaluation.EvaluationType, evaluation.Status,
        evaluation.TotalScore, evaluation.Comments, audit?.EvidenceSummary,
        evaluation.EvaluatedAt, audit?.FinalizedBy, audit?.FinalizedAt,
        evaluation.CreatedAt, evaluation.UpdatedAt);
}

