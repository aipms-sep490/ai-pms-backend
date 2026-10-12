using System.Text.Json;
using System.Text.Json.Nodes;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Evaluations.Services;
using AIPMS.Application.Features.Results.DTOs;

namespace AIPMS.Application.Features.Results.Services;

// Recalculate from publication inputs, never today's rubric, evaluator or student profile.
public static class ResultRevisionCalculator
{
    private sealed record FrozenEvaluation(long Id, string ScopeSnapshotJson, EvaluationDraftRecord Evaluation);
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    private static ConflictException Unknown() => new("Published result lacks exact scoring provenance.", "RESULT_PROVENANCE_UNKNOWN");
    public static RevisionCalculation Calculate(string frozenInputs, IReadOnlyList<ScoreAmendmentInput> amendments)
    {
        try
        {
            var baseline = CalculateCore(frozenInputs, []);
            var original = JsonNode.Parse(frozenInputs)!["Breakdown"]!.Deserialize<ResultBreakdownDto>(Options)!;
            if (JsonSerializer.Serialize(baseline.Breakdown) != JsonSerializer.Serialize(original)) throw Unknown();
            return amendments.Count == 0 ? baseline : CalculateCore(frozenInputs, amendments);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or NullReferenceException
            or FormatException or ArgumentException or ArithmeticException)
        { throw Unknown(); }
    }
    private static RevisionCalculation CalculateCore(string json, IReadOnlyList<ScoreAmendmentInput> amendments)
    {
        var root = JsonNode.Parse(json)?.AsObject() ?? throw Unknown();
        if (root["SnapshotVersion"]?.GetValue<int>() != 2) throw Unknown();
        var scheme = root["Scheme"]!.Deserialize<EvaluationSchemeDto>(Options) ?? throw Unknown();
        var policy = scheme.Policy ?? throw Unknown();
        if (scheme.Status != "PUBLISHED" || scheme.LockedAt is null || policy.IsDemo
            || policy.ApprovalStatus != "APPROVED" || string.IsNullOrWhiteSpace(policy.ApprovalReference)) throw Unknown();
        try
        {
            var componentInputs = scheme.Components.Select(c => new SchemeComponentInput(c.Name, c.Scope,
                c.MajorId, c.RubricId, c.ProjectWeightPercent, c.StudentWeightPercent, c.RequiredEvaluators, c.EvaluatorWeights, c.Stage!)).ToArray();
            var majorIds = scheme.Students.Select(s => s.MajorId).Distinct().ToArray();
            EvaluationSchemeRules.Validate(componentInputs, majorIds, scheme.PassThreshold);
            VersionedResultCalculator.Validate(policy, componentInputs, majorIds);
        }
        catch (ValidationException) { throw Unknown(); }
        if (scheme.CalculationRule != (policy.CalculationVersion == "LEGACY_V1" ? EvaluationSchemeRules.CalculationRule : VersionedResultCalculator.Version)
            || scheme.Components.Select(c => c.Id).Distinct().Count() != scheme.Components.Count
            || scheme.Components.Any(c => c.RequiredEvaluators <= 0 || c.Scope is not ("COMMON" or "MAJOR_SPECIFIC" or "INDIVIDUAL")
                || (c.Scope == "COMMON") != (c.MajorId is null) || c.Scope == "INDIVIDUAL" && c.ProjectWeightPercent != 0
                || c.ProjectWeightPercent < 0 || c.StudentWeightPercent < 0 || c.Stage is not ("COLD" or "DEFENSE" or "ON_GOING"))) throw Unknown();
        var target = root["Target"]?.Deserialize<SchemeStudent>(Options);
        if (target is not null && !scheme.Students.Contains(target)) throw Unknown();
        var package = root["PackageId"]!.GetValue<long>();
        if (package <= 0 || root["Policy"]!["Id"]!.GetValue<long>() != scheme.PolicyVersionId
            || root["Policy"]!["Status"]!.GetValue<string>() != "LOCKED") throw Unknown();
        var oldBreakdown = root["Breakdown"]!.Deserialize<ResultBreakdownDto>(Options) ?? throw Unknown();
        if (oldBreakdown.IsLegacy || oldBreakdown.Readiness != "READY" || oldBreakdown.CalculationVersion != policy.CalculationVersion) throw Unknown();
        var contributors = root["Contributions"]!.Deserialize<ResultContributionDto[]>(Options) ?? throw Unknown();
        var frozen = root["Evaluations"]!.Deserialize<FrozenEvaluation[]>(Options) ?? throw Unknown();
        if (contributors.Select(c => c.EvaluationId).Distinct().Count() != contributors.Length) throw Unknown();
        if (amendments.Any(a => a is null || a.EvaluationId <= 0 || a.CriterionId <= 0 || string.IsNullOrWhiteSpace(a.Reason) || a.Reason.Length > 2000)
            || amendments.Select(a => (a.EvaluationId, a.CriterionId)).Distinct().Count() != amendments.Count)
            throw new ValidationException(new Dictionary<string, string[]> { ["amendments"] = ["Unique evaluation/criterion and reason <=2000 required."] });
        var contributions = new List<ResultContributionDto>();
        var byComponent = new Dictionary<long, List<(decimal Score, decimal Weight)>>();
        var evaluatorSlots = new HashSet<(long Component, long Evaluator)>();
        var changed = new Dictionary<long, EvaluationDraftRecord>();
        foreach (var original in contributors)
        {
            var records = frozen.Where(f => f.Evaluation.Id == original.EvaluationId && f.Id == original.AssignmentId).ToArray();
            if (records.Length == 0) throw Unknown();
            var row = records[0];
            if (records.Any(r => JsonSerializer.Serialize(r) != JsonSerializer.Serialize(row))) throw Unknown();
            var evaluation = row.Evaluation;
            using var assignment = JsonDocument.Parse(row.ScopeSnapshotJson);
            var componentId = assignment.RootElement.GetProperty("component").GetProperty("Id").GetInt64();
            var component = scheme.Components.Single(c => c.Id == componentId);
            var pinnedScheme = assignment.RootElement.GetProperty("Scheme").Deserialize<EvaluationSchemeDto>(Options)!;
            var pinnedComponent = assignment.RootElement.GetProperty("component").Deserialize<SchemeComponentDto>(Options)!;
            // ORM component snapshots contain persisted weight JSON, so compare stable authority fields explicitly.
            if (JsonSerializer.Serialize(pinnedScheme) != JsonSerializer.Serialize(scheme)
                || pinnedComponent.Scope != component.Scope || pinnedComponent.MajorId != component.MajorId
                || pinnedComponent.RubricId != component.RubricId || pinnedComponent.Stage != component.Stage
                || pinnedComponent.RequiredEvaluators != component.RequiredEvaluators
                || pinnedComponent.ProjectWeightPercent != component.ProjectWeightPercent
                || pinnedComponent.StudentWeightPercent != component.StudentWeightPercent
                || !evaluatorSlots.Add((componentId, original.EvaluatorId))) throw Unknown();
            var assignedStudent = assignment.RootElement.GetProperty("StudentId");
            if (evaluation.Status != "FINALIZED" || evaluation.Finalization?.Evidence.FinalSubmissionId != package
                || evaluation.AssignmentId != row.Id || evaluation.EvaluatorId != original.EvaluatorId
                || evaluation.RubricId != component.RubricId || original.RubricId != component.RubricId
                || evaluation.ProjectId != scheme.ProjectId || evaluation.Finalization.Evidence.Stage != component.Stage
                || component.Scope == "INDIVIDUAL" && (target is null || assignedStudent.GetInt64() != target.StudentId)
                || component.Scope != "COMMON" && target is not null && component.MajorId != target.MajorId
                || component.Scope != "INDIVIDUAL" && assignedStudent.ValueKind != JsonValueKind.Null
                || assignment.RootElement.GetProperty("FinalSubmissionId").GetInt64() != package
                || evaluation.Finalization.FinalizedBy != evaluation.EvaluatorId
                || evaluation.Finalization.Evidence.ProjectPeriodId <= 0
                || evaluation.Finalization.Evidence.FileCount <= 0 || evaluation.Finalization.Evidence.ArtifactCount <= 0
                || evaluation.Finalization.Evidence.DeliverableVersionIds.Count == 0
                || original.EvaluationConcurrencyToken != evaluation.ConcurrencyToken
                || EvaluationScoring.FinalTotal(evaluation.Scores) != evaluation.TotalScore) throw Unknown();
            if (component.Stage == "COLD")
            {
                var cold = assignment.RootElement.GetProperty("Cold");
                var evidence = evaluation.Finalization.Evidence;
                if (cold.GetProperty("PackageId").GetInt64() != evidence.ColdPackageId
                    || string.IsNullOrWhiteSpace(evidence.ColdEvidenceHash) || cold.GetProperty("Hash").GetString() != evidence.ColdEvidenceHash
                    || evidence.ColdSubmissionIds is null || evidence.ColdSubmissionIds.Count == 0
                    || !cold.GetProperty("SubmissionIds").Deserialize<long[]>()!.SequenceEqual(evidence.ColdSubmissionIds)) throw Unknown();
            }
            if (VersionedResultCalculator.EvaluationTotal(evaluation.Scores, policy.CalculationVersion) != original.Score) throw Unknown();
            var edits = amendments.Where(a => a.EvaluationId == evaluation.Id).ToDictionary(a => a.CriterionId);
            if (edits.Keys.Any(id => evaluation.Scores.All(c => c.RubricCriterionId != id))) throw Unknown();
            var scores = evaluation.Scores.Select(c => edits.TryGetValue(c.RubricCriterionId, out var edit) ? c with { Score = edit.Score } : c).ToArray();
            var total = VersionedResultCalculator.EvaluationTotal(scores, policy.CalculationVersion);
            changed[evaluation.Id] = evaluation with { Scores = scores, TotalScore = EvaluationScoring.FinalTotal(scores) };
            var weight = component.EvaluatorWeights?.Single(w => w.EvaluatorId == evaluation.EvaluatorId).WeightPercent
                ?? 100m / component.RequiredEvaluators;
            if (original.WeightPercent != (target is null ? component.ProjectWeightPercent : component.StudentWeightPercent) * weight / 100m) throw Unknown();
            if (!byComponent.TryGetValue(componentId, out var values)) byComponent[componentId] = values = [];
            values.Add((total, weight));
            contributions.Add(original with { Score = total });
        }
        if (amendments.Any(a => contributors.All(c => c.EvaluationId != a.EvaluationId)))
            throw new ConflictException("An amendment targets another result.", "AMENDMENT_TARGET_MISMATCH");
        var expectedComponents = scheme.Components.Where(c => target is null ? c.ProjectWeightPercent > 0
            : c.StudentWeightPercent > 0 && (c.Scope == "COMMON" || c.MajorId == target.MajorId)).OrderBy(c => c.Id).ToArray();
        if (!expectedComponents.Select(c => c.Id).Order().SequenceEqual(byComponent.Keys.Order())) throw Unknown();
        var components = expectedComponents.Select(c => {
            var values = byComponent[c.Id]; if (values.Count != c.RequiredEvaluators) throw Unknown();
            return new ResultComponentScoreDto(c.Id, c.Name, c.Scope, c.MajorId,
                target is null ? c.ProjectWeightPercent : c.StudentWeightPercent,
                c.EvaluatorWeights is null ? values.Average(v => v.Score) : values.Sum(v => v.Score * v.Weight / 100m),
                c.RequiredEvaluators, values.Count, c.Stage);
        }).ToArray();
        var raw = VersionedResultCalculator.Total(components, policy, target is not null) ?? throw Unknown();
        var final = VersionedResultCalculator.Round(raw, policy);
        var common = components.Where(c => c.Scope == "COMMON").ToArray();
        var major = components.Where(c => c.Scope == "MAJOR_SPECIFIC").ToArray();
        var majors = major.GroupBy(c => c.MajorId!.Value).OrderBy(g => g.Key)
            .Select(g => new ResultMajorScoreDto(g.Key, g.Sum(c => c.Score!.Value * c.WeightPercent) / g.Sum(c => c.WeightPercent))).ToArray();
        var breakdown = oldBreakdown with { UnroundedTotal = raw, Components = components, MajorBreakdown = majors,
            CommonScore = common.Length == 0 ? null : common.Sum(c => c.Score!.Value * c.WeightPercent) / common.Sum(c => c.WeightPercent),
            MajorAggregate = major.Length == 0 ? null : major.Sum(c => c.Score!.Value * c.WeightPercent) / major.Sum(c => c.WeightPercent) };
        root["Breakdown"] = JsonSerializer.SerializeToNode(breakdown);
        root["Contributions"] = JsonSerializer.SerializeToNode(contributions);
        foreach (var entry in root["Evaluations"]!.AsArray())
        {
            var node = entry!.AsObject(); var id = node["evaluation"]!["Id"]!.GetValue<long>();
            if (changed.TryGetValue(id, out var value)) node["evaluation"] = JsonSerializer.SerializeToNode(value);
        }
        return new(root.ToJsonString(), final, breakdown, contributions);
    }
}
