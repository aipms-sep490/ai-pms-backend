using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Domain.Exceptions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Evaluations.Services;
using AIPMS.Application.Features.Notifications.Events;
using AIPMS.Application.Features.Results.DTOs;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed partial class EvaluationSchemeService
{
    private sealed record ResultCheck(ProjectResultPreviewDto Preview, EvaluationScheme Scheme, long PackageId,
        long? MajorId, string FrozenInputs);
    private async Task<ResultCheck> Check(long projectId, long? studentId, CancellationToken ct, bool publishing = false)
    {
        await Manage(projectId, ct);
        var actor = (await evaluations.GetActorAsync(Actor, ct))!;
        var project = (await evaluations.GetProjectAsync(projectId, ct))!;
        if (!AcademicAuthority.HasScope(actor, project) || publishing && !AcademicAuthority.IsLead(actor, project))
            throw new ForbiddenException("Only the frozen lead department may publish project or student results.");
        var scheme = await db.Set<EvaluationScheme>().Include(x => x.Components)
            .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Status == "PUBLISHED", ct)
            ?? throw new ConflictException("A published scoped evaluation scheme is required. Legacy results remain read-only.");
        var students = JsonSerializer.Deserialize<SchemeStudent[]>(scheme.StudentsJson)!;
        var calculation = Policy(scheme);
        if (scheme.CalculationRule is not (EvaluationSchemeRules.CalculationRule or VersionedResultCalculator.Version or VersionedResultCalculator.PolV4Version))
            throw new ConflictException("This calculation rule is not supported by the current scorer.");
        var target = studentId.HasValue ? students.SingleOrDefault(x => x.StudentId == studentId)
            ?? throw new NotFoundException("StudentResult", studentId) : null;
        var components = scheme.Components.Where(c => target is null ? c.ProjectWeightPercent > 0
            : c.StudentWeightPercent > 0 && (c.Scope == "COMMON" || c.MajorId == target.MajorId)).OrderBy(c => c.Id).ToArray();
        var registration = ProjectAcademicScopeReader.ParseEvidence(scheme.RegistrationSnapshotJson)
            ?? throw new ConflictException("Frozen academic scope is unknown. Legacy results remain read-only.");
        if (calculation is not null)
        {
            VersionedResultCalculator.Validate(calculation, scheme.Components.Select(c => new SchemeComponentInput(c.Name,
                c.Scope, c.MajorId, c.RubricId, c.ProjectWeightPercent, c.StudentWeightPercent, c.RequiredEvaluators,
                Weights(c), c.Stage ?? "DEFENSE", c.TargetScope, c.IsIntegration)).ToArray(),
                registration.Scope.Requirements.Select(r => r.MajorId).Distinct().ToArray());
            if (scheme.CalculationRule != VersionedResultCalculator.CalculationRuleFor(calculation))
                throw new ConflictException("Scheme calculation metadata is inconsistent.");
        }
        var publicationForbidden = !AcademicAuthority.IsLead(actor, project);
        if (publicationForbidden)
        {
            var targetDepartment = target?.DepartmentId;
            var componentRubricIds = components.Where(c => c.RubricId.HasValue).Select(c => c.RubricId!.Value).ToArray();
            var outsideReadScope = target is not null ? targetDepartment != actor.DepartmentId
                : await db.Rubrics.AnyAsync(r => componentRubricIds.Contains(r.Id) && r.DepartmentId != actor.DepartmentId, ct);
            if (outsideReadScope || publishing || !AcademicAuthority.HasScope(actor, project))
                throw new ForbiddenException("Only the frozen lead department may publish project or student results.");
        }
        var package = await submissions.GetAsync(projectId, ct) ?? throw new ConflictException("Locked final package required.");
        var blockers = new List<string>();
        if (calculation is null) blockers.Add("ACADEMIC_APPROVAL_UNKNOWN");
        else if (calculation.IsDemo) blockers.Add("DEMO_SCHEME_NOT_OFFICIAL");
        else if (calculation.ApprovalStatus != "APPROVED" || string.IsNullOrWhiteSpace(calculation.ApprovalReference))
            blockers.Add("ACADEMIC_APPROVAL_REQUIRED");
        if (publicationForbidden) blockers.Add("LEAD_DEPARTMENT_REQUIRED_FOR_PUBLICATION");
        if (!project.ActiveScope) blockers.Add("ACADEMIC_SCOPE_INACTIVE");
        var frozenPolicy = await db.Set<PeriodPolicyVersion>().AsNoTracking().SingleOrDefaultAsync(p => p.Id == scheme.PolicyVersionId, ct);
        if (frozenPolicy is null || frozenPolicy.Status != "LOCKED" || frozenPolicy.ProjectPeriodId != scheme.ProjectPeriodId)
            blockers.Add("FROZEN_POLICY_REQUIRED");
        if (package.Items.Count == 0 || package.Items.Any(i => i.Files.Count == 0)) blockers.Add("LOCKED_PACKAGE_REQUIRED");
        if (project.Status != "FINAL_SUBMISSION" && !(studentId.HasValue && project.Status == "COMPLETED")) blockers.Add("PROJECT_NOT_FINAL_SUBMISSION");
        if (target is null ? await results.GetAsync(projectId, ct) is not null
            : await db.Set<StudentResult>().AnyAsync(r => r.ProjectId == projectId && r.StudentId == studentId, ct)) blockers.Add("ALREADY_PUBLISHED");
        var allAssignments = await db.Set<EvaluationAssignment>().AsNoTracking().Where(a => a.ProjectId == projectId && a.Status == "ACTIVE"
            && a.ComponentId != null).OrderBy(a => a.Id).ToListAsync(ct);
        // Batch immutable finalizations once. Never recompute history from mutable rubric/detail rows.
        var finalizations = await (from state in db.Set<EvaluationDraftState>().AsNoTracking()
            join finalization in db.Set<EvaluationFinalization>().AsNoTracking() on state.EvaluationId equals finalization.EvaluationId
            join assignment in db.Set<EvaluationAssignment>() on state.AssignmentId equals assignment.Id
            where assignment.ProjectId == projectId && assignment.Status == "ACTIVE"
            select new { state.AssignmentId, finalization.SnapshotJson }).ToListAsync(ct);
        var finalized = finalizations.ToDictionary(f => f.AssignmentId,
            f => JsonSerializer.Deserialize<EvaluationDraftRecord>(f.SnapshotJson));
        var invalidCommitteeAssignments = new HashSet<long>();
        foreach (var assignment in allAssignments.Where(a => a.Stage is "COLD" or "DEFENSE"))
        {
            // Already-finalized evaluations were verified at finalization time; preserve immutable history
            // when another evaluator on the committee is replaced later (TK-W3-07).
            if (finalized.ContainsKey(assignment.Id)) continue;
            try { await DefenseCommitteeAuthority.RequirePinnedAsync(db, assignment, ct); }
            catch (ConflictException) { invalidCommitteeAssignments.Add(assignment.Id); blockers.Add($"COMMITTEE_BINDING_INVALID:{assignment.Id}"); }
            catch (ForbiddenException) { invalidCommitteeAssignments.Add(assignment.Id); blockers.Add($"COMMITTEE_MEMBER_INVALID:{assignment.Id}"); }
        }
        var coldBindings = new Dictionary<long, ColdEvidenceBinding>();
        var coldAssignments = allAssignments.Where(a => a.Stage == "COLD" && scheme.Components.Any(c => c.Id == a.ComponentId
            && (target is null ? c.ProjectWeightPercent > 0 || c.StudentWeightPercent > 0
                : c.StudentWeightPercent > 0 && (c.Scope == "COMMON" || c.MajorId == target.MajorId)
                    && (c.Scope != "INDIVIDUAL" || a.StudentId == studentId)))).ToArray();
        ColdEvidenceSet? coldEvidence = null;
        if (coldAssignments.Length > 0)
        {
            try { coldEvidence = await ColdEvaluationBinding.Load(db, projectId, ct); }
            catch (ConflictException) { blockers.Add("COLD_PACKAGE_REQUIRED"); }
        }
        foreach (var assignment in coldAssignments)
        {
            try { if (coldEvidence is not null) coldBindings[assignment.Id] = ColdEvaluationBinding.Validate(coldEvidence, assignment); }
            catch (ConflictException) { blockers.Add($"COLD_EVIDENCE_CHANGED:{assignment.Id}"); }
        }
        var gateScores = await db.Set<GateIndividualProcessScore>().AsNoTracking()
            .Where(g => g.ProjectId == projectId).ToListAsync(ct);
        var gateNumbers = await db.ProjectGateInstances.AsNoTracking()
            .Where(g => g.ProjectId == projectId)
            .Select(g => new { g.Id, g.Sequence })
            .ToDictionaryAsync(g => g.Id, g => g.Sequence, ct);
        var absences = await db.Set<StudentDefenseAbsence>().AsNoTracking()
            .Where(a => a.ProjectId == projectId && a.RevokedAt == null)
            .ToDictionaryAsync(a => a.StudentId, ct);
        var contributions = new List<ResultContributionDto>();
        var componentResults = new List<ResultComponentScoreDto>();
        var publishedFeedback = new List<ResultFeedbackItemDto>();
        var frozen = new List<object>();
        ResultComponentScoreDto Calculate(EvaluationSchemeComponent component, long? forStudent, bool includeContributions)
        {
            var weight = forStudent.HasValue ? component.StudentWeightPercent : component.ProjectWeightPercent;
            var assignments = allAssignments.Where(a => a.ComponentId == component.Id
                && a.StudentId == (component.Scope == "INDIVIDUAL" ? forStudent : null)).ToArray();

            // Explicit defense absence (Spec V4.0 §7.5): 0.00 for that individual student only without blocking teammates.
            if (forStudent.HasValue && component.Stage == "DEFENSE" && absences.TryGetValue(forStudent.Value, out var absence))
            {
                frozen.Add(new { Type = "DEFENSE_ABSENT", ComponentId = component.Id, absence.Id, absence.StudentId, absence.Reason, absence.MinutesReference, absence.RecordedAt });
                if (includeContributions)
                    publishedFeedback.Add(new(component.Id, component.Name, component.Stage, $"DEFENSE_ABSENT: {absence.Reason}"));
                return new(component.Id, component.Name, component.Scope, component.MajorId, weight, 0.00m,
                    component.RequiredEvaluators, component.RequiredEvaluators, component.Stage, component.TargetScope, component.IsIntegration);
            }

            // POL-03: Cumulative 20% Individual Process score from Primary Supervisor across Gates G1-G3.
            if (forStudent.HasValue && component.Scope == "INDIVIDUAL" && component.Stage == "ON_GOING"
                && assignments.Length == 0 && calculation?.CalculationVersion == VersionedResultCalculator.PolV4Version)
            {
                var configuredGates = VersionedResultCalculator.EffectiveGateWeights(calculation);
                var studentGates = gateScores.Where(g => g.StudentId == forStudent.Value).ToList();
                var matched = new List<(GateIndividualProcessScore Row, int GateNumber, decimal GateWeight)>();
                foreach (var gw in configuredGates)
                {
                    var row = studentGates.FirstOrDefault(g => gateNumbers.TryGetValue(g.GateInstanceId, out var num) && num == gw.GateNumber);
                    if (row is null || row.Status != "FINALIZED" || !row.Score.HasValue)
                    {
                        blockers.Add($"INDIVIDUAL_PROCESS_SCORE_MISSING:{forStudent.Value}:G{gw.GateNumber}");
                        continue;
                    }
                    matched.Add((row, gw.GateNumber, gw.WeightPercent));
                }
                if (matched.Count == configuredGates.Count)
                {
                    var cumulative = matched.Sum(m => m.Row.Score!.Value * m.GateWeight / 100m);
                    frozen.Add(new
                    {
                        Type = "GATE_INDIVIDUAL_PROCESS",
                        ComponentId = component.Id,
                        StudentId = forStudent.Value,
                        Gates = matched.Select(m => new { m.Row.Id, m.GateNumber, m.Row.Score, m.GateWeight, m.Row.SupervisorAssignmentId, m.Row.FinalizedAt }).ToArray()
                    });
                    if (includeContributions)
                    {
                        foreach (var m in matched.Where(x => !string.IsNullOrWhiteSpace(x.Row.Comments)))
                            publishedFeedback.Add(new(component.Id, $"{component.Name} (G{m.GateNumber})", component.Stage, m.Row.Comments!.Trim()));
                    }
                    return new(component.Id, component.Name, component.Scope, component.MajorId, weight, cumulative,
                        component.RequiredEvaluators, component.RequiredEvaluators, component.Stage, component.TargetScope, component.IsIntegration);
                }
                return new(component.Id, component.Name, component.Scope, component.MajorId, weight, null,
                    component.RequiredEvaluators, 0, component.Stage, component.TargetScope, component.IsIntegration);
            }

            if (assignments.Length != component.RequiredEvaluators) blockers.Add($"MISSING_EVALUATOR:{component.Id}");
            var componentScores = new List<(decimal Score, decimal Weight)>();
            var explicitWeights = Weights(component);
            foreach (var assignment in assignments)
            {
                if (invalidCommitteeAssignments.Contains(assignment.Id)) continue;
                var evaluation = finalized.GetValueOrDefault(assignment.Id);
                if (evaluation?.Finalization is null || evaluation.Status != "FINALIZED")
                { blockers.Add($"EVALUATION_NOT_FINALIZED:{assignment.Id}"); continue; }
                if (assignment.Scope != component.Scope || assignment.MajorId != component.MajorId || assignment.RubricId != component.RubricId
                    || assignment.Stage != component.Stage || evaluation.Finalization.Evidence.Stage != assignment.Stage
                    || assignment.PolicyVersionId != scheme.PolicyVersionId || assignment.ProjectPeriodId != scheme.ProjectPeriodId
                    || evaluation.AssignmentId != assignment.Id || evaluation.ProjectId != projectId || evaluation.EvaluatorId != assignment.EvaluatorId
                    || evaluation.RubricId != component.RubricId || evaluation.Finalization.Evidence.FinalSubmissionId != package.Id)
                { blockers.Add($"SNAPSHOT_MISMATCH:{assignment.Id}"); continue; }
                if (assignment.Stage == "COLD" && (!coldBindings.TryGetValue(assignment.Id, out var binding)
                    || evaluation.Finalization.Evidence.ColdPackageId != binding.PackageId
                    || evaluation.Finalization.Evidence.ColdEvidenceHash != binding.Hash
                    || !(evaluation.Finalization.Evidence.ColdSubmissionIds ?? []).SequenceEqual(binding.SubmissionIds)))
                { blockers.Add($"COLD_SNAPSHOT_MISMATCH:{assignment.Id}"); continue; }
                if (EvaluationScoring.FinalTotal(evaluation.Scores) != evaluation.TotalScore)
                { blockers.Add($"TOTAL_MISMATCH:{assignment.Id}"); continue; }
                var evaluatorWeight = explicitWeights?.SingleOrDefault(w => w.EvaluatorId == assignment.EvaluatorId)?.WeightPercent
                    ?? (explicitWeights is null ? 100m / component.RequiredEvaluators : 0m);
                if (evaluatorWeight <= 0) { blockers.Add($"EVALUATOR_WEIGHT_MISMATCH:{assignment.Id}"); continue; }
                var total = VersionedResultCalculator.EvaluationTotal(evaluation.Scores, calculation?.CalculationVersion ?? "LEGACY_V1");
                componentScores.Add((total, evaluatorWeight));
                if (includeContributions)
                {
                    contributions.Add(new(assignment.Id, evaluation.Id, evaluation.EvaluatorId, evaluation.RubricId,
                        weight * evaluatorWeight / 100m, total, evaluation.ConcurrencyToken));
                    if (forStudent.HasValue && calculation?.CalculationVersion == VersionedResultCalculator.PolV4Version
                        && !string.IsNullOrWhiteSpace(evaluation.Comments))
                        publishedFeedback.Add(new(component.Id, component.Name, component.Stage, evaluation.Comments.Trim()));
                }
                frozen.Add(new { assignment.Id, assignment.ScopeSnapshotJson, evaluation });
            }
            decimal? mean = componentScores.Count == component.RequiredEvaluators && assignments.Length == component.RequiredEvaluators
                ? explicitWeights is null ? componentScores.Average(s => s.Score)
                    : componentScores.Sum(s => s.Score * s.Weight / 100m) : null;
            return new(component.Id, component.Name, component.Scope, component.MajorId, weight, mean,
                component.RequiredEvaluators, componentScores.Count, component.Stage, component.TargetScope, component.IsIntegration);
        }
        foreach (var component in components) componentResults.Add(Calculate(component, studentId, true));
        // Completing the project closes scoring, so all mandatory student slots must already be finalized too.
        if (target is null)
            foreach (var component in scheme.Components.Where(c => c.StudentWeightPercent > 0))
            {
                var targets = component.Scope == "INDIVIDUAL" ? students.Where(s => s.MajorId == component.MajorId).Select(s => (long?)s.StudentId).ToArray() : [null];
                foreach (var student in targets)
                {
                    if (components.All(c => c.Id != component.Id)) Calculate(component, student, false);
                }
            }
        var scoreReady = blockers.All(b => b is "LEAD_DEPARTMENT_REQUIRED_FOR_PUBLICATION" or "DEMO_SCHEME_NOT_OFFICIAL"
            or "ACADEMIC_APPROVAL_REQUIRED" or "ACADEMIC_APPROVAL_UNKNOWN" or "ALREADY_PUBLISHED");
        var rawTotal = scoreReady && calculation is not null ? VersionedResultCalculator.Total(componentResults, calculation, studentId.HasValue) : null;
        decimal? final = rawTotal.HasValue ? VersionedResultCalculator.Round(rawTotal.Value, calculation!) : null;
        var evaluatorIds = contributions.Select(c => c.EvaluatorId).Distinct().ToArray();
        if (evaluatorIds.Length > 0)
        {
            var evaluatorNames = await db.Users.AsNoTracking().Where(u => evaluatorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
            for (var i = 0; i < contributions.Count; i++)
                contributions[i] = contributions[i] with { EvaluatorName = evaluatorNames.GetValueOrDefault(contributions[i].EvaluatorId) };
        }
        var common = componentResults.Where(c => c.Scope == "COMMON").ToArray();
        var majorScoreIds = componentResults.Where(c => c.Scope == "MAJOR_SPECIFIC" && c.MajorId.HasValue)
            .Select(c => c.MajorId!.Value).Distinct().ToArray();
        var majorScoreNames = majorScoreIds.Length == 0 ? new Dictionary<long, string>()
            : await db.Majors.AsNoTracking().Where(m => majorScoreIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => m.Name, ct);
        var majorScores = componentResults.Where(c => c.Scope == "MAJOR_SPECIFIC").GroupBy(c => c.MajorId!.Value)
            .OrderBy(g => g.Key).Select(g => new ResultMajorScoreDto(g.Key, g.All(c => c.Score.HasValue)
                ? g.Sum(c => c.Score!.Value * c.WeightPercent) / g.Sum(c => c.WeightPercent) : null,
                majorScoreNames.GetValueOrDefault(g.Key))).ToArray();
        var formulaComponents = new List<EvaluationFormulaComponentDto>();
        if (studentId.HasValue && target is not null)
        {
            var studentMajorId = target.MajorId;
            var commonWeight = components.Where(c => c.Scope == "COMMON").Sum(c => c.StudentWeightPercent);
            var majorWeight = components.Where(c => c.Scope == "MAJOR_SPECIFIC" && c.MajorId == studentMajorId).Sum(c => c.StudentWeightPercent);
            var individualWeight = components.Where(c => c.Scope == "INDIVIDUAL" && c.MajorId == studentMajorId).Sum(c => c.StudentWeightPercent);

            if (commonWeight > 0) formulaComponents.Add(new EvaluationFormulaComponentDto("COMMON", commonWeight));
            if (majorWeight > 0) formulaComponents.Add(new EvaluationFormulaComponentDto("MAJOR", majorWeight));
            if (individualWeight > 0) formulaComponents.Add(new EvaluationFormulaComponentDto("INDIVIDUAL", individualWeight));
        }
        else
        {
            var commonWeight = components.Where(c => c.Scope == "COMMON").Sum(c => c.ProjectWeightPercent);
            var majorWeight = components.Where(c => c.Scope == "MAJOR_SPECIFIC").Sum(c => c.ProjectWeightPercent);
            var individualWeight = components.Where(c => c.Scope == "INDIVIDUAL").Sum(c => c.ProjectWeightPercent);

            if (commonWeight > 0) formulaComponents.Add(new EvaluationFormulaComponentDto("COMMON", commonWeight));
            if (majorWeight > 0) formulaComponents.Add(new EvaluationFormulaComponentDto("MAJOR", majorWeight));
            if (individualWeight > 0) formulaComponents.Add(new EvaluationFormulaComponentDto("INDIVIDUAL", individualWeight));
        }
        var formula = formulaComponents.Count > 0 ? new EvaluationFormulaDto(formulaComponents, null) : null;

        var isDefenseAbsent = studentId.HasValue && absences.ContainsKey(studentId.Value);
        (string? Outcome, string? OutcomeDetail) evaluated = final.HasValue
            ? VersionedResultCalculator.EvaluateOutcome(final.Value, scheme.PassThreshold, componentResults, calculation, isDefenseAbsent)
            : (null, null);

        var breakdown = calculation is null ? ResultSnapshotReader.Legacy(scheme.CalculationRule)
            : new ResultBreakdownDto(scheme.Name, scheme.Version, calculation.CalculationVersion, false, calculation.IsDemo,
                calculation.ApprovalStatus, final.HasValue ? "READY" : "PENDING", rawTotal,
                common.Length > 0 && common.All(c => c.Score.HasValue) ? common.Sum(c => c.Score!.Value * c.WeightPercent) / common.Sum(c => c.WeightPercent) : null,
                majorScores.Length > 0 && majorScores.All(m => m.Score.HasValue)
                    ? calculation.Formula == "DEMO_50_30_20" ? majorScores.Average(m => m.Score!.Value)
                        : componentResults.Where(c => c.Scope == "MAJOR_SPECIFIC").Sum(c => c.Score!.Value * c.WeightPercent)
                            / componentResults.Where(c => c.Scope == "MAJOR_SPECIFIC").Sum(c => c.WeightPercent) : null,
                majorScores, componentResults, formula,
                evaluated.OutcomeDetail,
                publishedFeedback.Count > 0 ? publishedFeedback : null);
        var warnings = new List<ResultWarningDto>();
        if (calculation?.Formula == "POL_INTER_40_40_20")
        {
            var floor = VersionedResultCalculator.EffectiveFailingFloor(calculation);
            if (floor.HasValue)
            {
                foreach (var comp in componentResults.Where(c => c.Scope == "COMMON" && c.Stage == "DEFENSE" && c.Score.HasValue))
                {
                    var roundedScore = decimal.Round(comp.Score!.Value, 2, MidpointRounding.AwayFromZero);
                    if (roundedScore < floor.Value)
                    {
                        warnings.Add(new ResultWarningDto(
                            WorkflowErrorCodes.InterdisciplinaryDefenseCommonBelowFloor,
                            $"Điểm Bảo vệ phần Chung ({comp.Name}: {roundedScore:0.00}) dưới ngưỡng {floor.Value:0.00}. Theo POL-01 liên ngành, điều kiện điểm liệt được xét trên tổng khối 40% Chung (không chặn công bố nếu tổng khối Chung đạt >= {floor.Value:0.00}). Hội đồng cần rà soát kỹ trước khi công bố.",
                            comp.ComponentId,
                            roundedScore,
                            floor.Value));
                    }
                }
            }
        }

        var inputs = JsonSerializer.Serialize(new { SnapshotVersion = 2, Breakdown = breakdown, Scheme = Map(scheme), scheme.RegistrationSnapshotJson, Target = target,
            Policy = frozenPolicy is null ? null : new { frozenPolicy.Id, frozenPolicy.Version, frozenPolicy.Status, frozenPolicy.SnapshotJson },
            PackageId = package.Id, project.Status, Contributions = contributions, Evaluations = frozen, Blockers = blockers });
        var token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(inputs)));
        var preview = new ProjectResultPreviewDto(projectId, final.HasValue && blockers.Count == 0, blockers.Distinct().ToArray(), final, scheme.PassThreshold,
            evaluated.Outcome, token, contributions, breakdown, evaluated.OutcomeDetail, warnings.Count > 0 ? warnings : null);
        return new(preview, scheme, package.Id, target?.MajorId, inputs);
    }
    public Task<ProjectResultPreviewDto> PreviewAsync(long projectId, long? studentId, CancellationToken ct) => evaluations.InTransactionAsync(async () =>
    { await evaluations.LockProjectAsync(projectId, ct); return (await Check(projectId, studentId, ct)).Preview; }, ct);
    private static bool IsIncompleteScoreBlocker(string b) =>
        b.StartsWith("MISSING_EVALUATOR:", StringComparison.Ordinal)
        || b.StartsWith("EVALUATION_NOT_FINALIZED:", StringComparison.Ordinal)
        || b.StartsWith("INDIVIDUAL_PROCESS_SCORE_MISSING:", StringComparison.Ordinal)
        || b.StartsWith("COLD_GRADING_PENDING:", StringComparison.Ordinal);
    private static void Confirm(ResultCheck check, string token, bool requireToken = true)
    {
        if (!check.Preview.CanPublish)
        {
            if (!check.Preview.Blockers.Contains("ALREADY_PUBLISHED") && check.Preview.Blockers.Any(IsIncompleteScoreBlocker))
                throw new AIPMS.Domain.Exceptions.DomainException(
                    "Cannot publish result while required scores or finalized evaluations are missing: " + string.Join(", ", check.Preview.Blockers),
                    WorkflowErrorCodes.IncompleteScores,
                    new ProblemMetadata(BlockingRequirements: check.Preview.Blockers.Select(b => new BlockingRequirement(b, b)).ToArray()));
            throw new ConflictException(string.Join(", ", check.Preview.Blockers));
        }
        if (requireToken && !string.Equals(check.Preview.ConfirmationToken, token, StringComparison.Ordinal))
            throw new ConflictException("Preview inputs changed. Reload before publication.");
    }
    public Task<ProjectResultDto> PublishProjectAsync(long projectId, string token, CancellationToken ct) => evaluations.InTransactionAsync(async () =>
    {
        await evaluations.LockProjectAsync(projectId, ct);
        var check = await Check(projectId, null, ct, publishing: true); Confirm(check, token); var now = Now;
        var saved = await results.PublishAsync(new(0, projectId, check.PackageId, check.Preview.TotalScore!.Value,
            check.Scheme.PassThreshold, check.Preview.Outcome!, check.Scheme.CalculationRule, Actor, now,
            check.Scheme.ConcurrencyToken.ToString("N"), check.Preview.Contributions, check.Scheme.Id, check.Scheme.PolicyVersionId, check.FrozenInputs,
            check.Preview.Breakdown, OutcomeDetail: check.Preview.OutcomeDetail), ct);
        await Audit("PROJECT_RESULT_PUBLISHED", "PROJECT_RESULT", saved.Id, new { Result = saved, check.FrozenInputs }, ct);
        await publisher.Publish(new WorkflowNotificationEvent(WorkflowNotificationKind.ProjectResultPublished, saved.Id, Actor, now), ct);
        return saved;
    }, ct);
    public Task<StudentResultDto> PublishStudentAsync(long projectId, long studentId, string token, CancellationToken ct) => evaluations.InTransactionAsync(async () =>
    {
        await evaluations.LockProjectAsync(projectId, ct);
        var check = await Check(projectId, studentId, ct, publishing: true); Confirm(check, token);
        var row = new StudentResult { ProjectId = projectId, StudentId = studentId, MajorId = check.MajorId!.Value,
            SchemeId = check.Scheme.Id, TotalScore = check.Preview.TotalScore!.Value, PassThreshold = check.Scheme.PassThreshold,
            Outcome = check.Preview.Outcome!, OutcomeDetail = check.Preview.OutcomeDetail, CalculationRule = check.Scheme.CalculationRule,
            PublishedBy = Actor, PublishedAt = Now, SnapshotJson = check.FrozenInputs };
        db.Add(row); await db.SaveChangesAsync(ct);
        db.AddRange(check.Preview.Contributions.Select(c => new StudentResultEvaluation { ResultId = row.Id, EvaluationId = c.EvaluationId }));
        await db.SaveChangesAsync(ct); await Audit("STUDENT_RESULT_PUBLISHED", "STUDENT_RESULT", row.Id, StudentDto(row), ct);
        await publisher.Publish(new WorkflowNotificationEvent(WorkflowNotificationKind.StudentResultPublished, row.Id, Actor, row.PublishedAt), ct);
        return StudentDto(row);
    }, ct);
    public Task<BatchPublishResultDto> PublishBatchAsync(long projectId, string token, CancellationToken ct) => evaluations.InTransactionAsync(async () =>
    {
        await evaluations.LockProjectAsync(projectId, ct);
        var projectCheck = await Check(projectId, null, ct, publishing: true);
        Confirm(projectCheck, token);
        var students = JsonSerializer.Deserialize<SchemeStudent[]>(projectCheck.Scheme.StudentsJson)!;
        var studentChecks = new List<(SchemeStudent Student, ResultCheck Check)>();
        foreach (var student in students)
        {
            var studentCheck = await Check(projectId, student.StudentId, ct, publishing: true);
            Confirm(studentCheck, studentCheck.Preview.ConfirmationToken, requireToken: false);
            studentChecks.Add((student, studentCheck));
        }
        var now = Now;
        var savedProject = await results.PublishAsync(new(0, projectId, projectCheck.PackageId, projectCheck.Preview.TotalScore!.Value,
            projectCheck.Scheme.PassThreshold, projectCheck.Preview.Outcome!, projectCheck.Scheme.CalculationRule, Actor, now,
            projectCheck.Scheme.ConcurrencyToken.ToString("N"), projectCheck.Preview.Contributions, projectCheck.Scheme.Id,
            projectCheck.Scheme.PolicyVersionId, projectCheck.FrozenInputs, projectCheck.Preview.Breakdown,
            OutcomeDetail: projectCheck.Preview.OutcomeDetail), ct);
        await Audit("PROJECT_RESULT_PUBLISHED", "PROJECT_RESULT", savedProject.Id, new { Result = savedProject, projectCheck.FrozenInputs, Batch = true }, ct);
        await publisher.Publish(new WorkflowNotificationEvent(WorkflowNotificationKind.ProjectResultPublished, savedProject.Id, Actor, now), ct);
        var savedStudents = new List<StudentResultDto>();
        foreach (var (student, sc) in studentChecks)
        {
            var row = new StudentResult
            {
                ProjectId = projectId,
                StudentId = student.StudentId,
                MajorId = sc.MajorId!.Value,
                SchemeId = sc.Scheme.Id,
                TotalScore = sc.Preview.TotalScore!.Value,
                PassThreshold = sc.Scheme.PassThreshold,
                Outcome = sc.Preview.Outcome!,
                OutcomeDetail = sc.Preview.OutcomeDetail,
                CalculationRule = sc.Scheme.CalculationRule,
                PublishedBy = Actor,
                PublishedAt = now,
                SnapshotJson = sc.FrozenInputs
            };
            db.Add(row);
            await db.SaveChangesAsync(ct);
            db.AddRange(sc.Preview.Contributions.Select(c => new StudentResultEvaluation { ResultId = row.Id, EvaluationId = c.EvaluationId }));
            await db.SaveChangesAsync(ct);
            var dto = StudentDto(row);
            await Audit("STUDENT_RESULT_PUBLISHED", "STUDENT_RESULT", row.Id, new { Result = dto, Batch = true }, ct);
            await publisher.Publish(new WorkflowNotificationEvent(WorkflowNotificationKind.StudentResultPublished, row.Id, Actor, now), ct);
            savedStudents.Add(dto);
        }
        return new BatchPublishResultDto(savedProject, savedStudents);
    }, ct);
    public Task<StudentDefenseAbsenceDto> RecordDefenseAbsenceAsync(long projectId, long studentId, RecordDefenseAbsenceRequest input, CancellationToken ct) =>
        evaluations.InTransactionAsync(async () =>
        {
            await evaluations.LockProjectAsync(projectId, ct);
            await LeadAuthority(projectId, ct);
            if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length > 2000
                || input.MinutesReference?.Trim().Length > 500)
                throw new ValidationException(new Dictionary<string, string[]> { ["reason"] = ["A reason (1..2000 chars) is required and minutesReference must be <= 500 chars."] });
            if (await db.Set<StudentResult>().AnyAsync(r => r.ProjectId == projectId && r.StudentId == studentId, ct))
                throw new ConflictException("Student result is already published.", "ALREADY_PUBLISHED");
            var isMember = await db.TeamMembers.AnyAsync(m => m.Team.Project != null && m.Team.Project.Id == projectId
                && m.UserId == studentId && m.LeftAt == null, ct);
            if (!isMember) throw new NotFoundException("Student", studentId);
            if (await db.Set<StudentDefenseAbsence>().AnyAsync(a => a.ProjectId == projectId && a.StudentId == studentId && a.RevokedAt == null, ct))
                throw new ConflictException("Defense absence has already been recorded for this student.", "DEFENSE_ABSENCE_ALREADY_RECORDED");
            var row = new StudentDefenseAbsence
            {
                ProjectId = projectId,
                StudentId = studentId,
                RecordedBy = Actor,
                Reason = input.Reason.Trim(),
                MinutesReference = string.IsNullOrWhiteSpace(input.MinutesReference) ? null : input.MinutesReference.Trim(),
                RecordedAt = Now
            };
            db.Add(row);
            await db.SaveChangesAsync(ct);
            var dto = new StudentDefenseAbsenceDto(row.Id, row.ProjectId, row.StudentId, row.RecordedBy,
                row.Reason, row.MinutesReference, DateTime.SpecifyKind(row.RecordedAt, DateTimeKind.Utc));
            await Audit("STUDENT_DEFENSE_ABSENCE_RECORDED", "STUDENT_DEFENSE_ABSENCE", row.Id, dto, ct);
            return dto;
        }, ct);
    private static StudentResultDto StudentDto(StudentResult r) => new(r.Id, r.ProjectId, r.StudentId, r.MajorId, r.SchemeId,
        r.TotalScore, r.PassThreshold, r.Outcome, r.CalculationRule, r.PublishedBy, DateTime.SpecifyKind(r.PublishedAt, DateTimeKind.Utc), r.SnapshotJson,
        ResultSnapshotReader.Read(r.SnapshotJson, r.CalculationRule, r.StudentId, r.MajorId), OutcomeDetail: r.OutcomeDetail);
    private Task<bool> IsActivePrimarySupervisorAsync(long projectId, long userId, CancellationToken ct) =>
        db.SupervisorAssignments.AsNoTracking().AnyAsync(a => a.ProjectId == projectId
            && a.IsPrimary && a.AssignmentType == "PRIMARY" && a.EndedAt == null
            && a.SupervisorProfile.UserId == userId, ct);

    public Task<StudentResultDto> GetStudentAsync(long projectId, long studentId, CancellationToken ct) => evaluations.InTransactionAsync(async () =>
    {
        var actor = await evaluations.GetActorAsync(Actor, ct) ?? throw new ForbiddenException();
        var isPrimarySupervisor = false;
        if (Actor != studentId)
        {
            if (actor.IsAdmin || actor.IsStaff)
            {
                await Manage(projectId, ct);
            }
            else
            {
                isPrimarySupervisor = await IsActivePrimarySupervisorAsync(projectId, Actor, ct);
                if (!isPrimarySupervisor) throw new ForbiddenException();
            }
        }
        var row = await PublishedResultResolver.Student(db, projectId, studentId, ct);
        if (row is null)
        {
            if ((Actor == studentId || isPrimarySupervisor) && !actor.IsAdmin && !actor.IsStaff)
                throw new ForbiddenException("Results have not been published yet.", "RESULT_NOT_PUBLISHED");
            throw new NotFoundException("StudentResult", studentId);
        }
        var canReadAuditSnapshot = Actor != studentId && actor.IsAdmin;
        if (Actor != studentId && !actor.IsAdmin && !isPrimarySupervisor)
        {
            var project = (await evaluations.GetProjectAsync(projectId, ct))!;
            canReadAuditSnapshot = AcademicAuthority.IsLead(actor, project);
            if (!canReadAuditSnapshot && ResultSnapshotReader.StudentDepartment(row.SnapshotJson, studentId) != actor.DepartmentId)
                throw new ForbiddenException();
        }
        // The publication audit snapshot contains the entire scheme roster, including other departments.
        return row with { SnapshotJson = canReadAuditSnapshot ? row.SnapshotJson : "{}" };
    }, ct);

    public Task<IReadOnlyList<StudentResultDto>> ListPublishedStudentsAsync(long projectId, CancellationToken ct) =>
        evaluations.InTransactionAsync<IReadOnlyList<StudentResultDto>>(async () =>
        {
            var actor = await evaluations.GetActorAsync(Actor, ct) ?? throw new ForbiddenException();
            var isPrimarySupervisor = false;
            var isAdminOrLead = false;
            if (actor.IsAdmin || actor.IsStaff)
            {
                await Manage(projectId, ct);
                var project = (await evaluations.GetProjectAsync(projectId, ct))!;
                isAdminOrLead = actor.IsAdmin || AcademicAuthority.IsLead(actor, project);
            }
            else
            {
                isPrimarySupervisor = await IsActivePrimarySupervisorAsync(projectId, Actor, ct);
                if (!isPrimarySupervisor) throw new ForbiddenException();
            }

            var studentIds = await db.Set<StudentResult>().AsNoTracking()
                .Where(r => r.ProjectId == projectId)
                .Select(r => r.StudentId)
                .Distinct()
                .OrderBy(id => id)
                .ToArrayAsync(ct);

            if (studentIds.Length == 0)
            {
                if (isPrimarySupervisor)
                    throw new ForbiddenException("Results have not been published yet.", "RESULT_NOT_PUBLISHED");
                return [];
            }

            var list = new List<StudentResultDto>(studentIds.Length);
            foreach (var sid in studentIds)
            {
                var resolved = await PublishedResultResolver.Student(db, projectId, sid, ct);
                if (resolved is null) continue;
                if (!isAdminOrLead && !isPrimarySupervisor
                    && ResultSnapshotReader.StudentDepartment(resolved.SnapshotJson, sid) != actor.DepartmentId)
                    continue;
                list.Add(resolved with { SnapshotJson = isAdminOrLead ? resolved.SnapshotJson : "{}" });
            }
            return list;
        }, ct);
}
