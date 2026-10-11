using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
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
        if (scheme.CalculationRule is not (EvaluationSchemeRules.CalculationRule or VersionedResultCalculator.Version))
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
                c.Scope, c.MajorId, c.RubricId, c.ProjectWeightPercent, c.StudentWeightPercent, c.RequiredEvaluators, Weights(c))).ToArray(),
                registration.Scope.Requirements.Select(r => r.MajorId).Distinct().ToArray());
            if (scheme.CalculationRule != (calculation.CalculationVersion == "LEGACY_V1"
                ? EvaluationSchemeRules.CalculationRule : VersionedResultCalculator.Version))
                throw new ConflictException("Scheme calculation metadata is inconsistent.");
        }
        var publicationForbidden = !AcademicAuthority.IsLead(actor, project);
        if (publicationForbidden)
        {
            var targetDepartment = target?.DepartmentId;
            var outsideReadScope = target is not null ? targetDepartment != actor.DepartmentId
                : await db.Rubrics.AnyAsync(r => components.Select(c => c.RubricId).Contains(r.Id) && r.DepartmentId != actor.DepartmentId, ct);
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
        var contributions = new List<ResultContributionDto>();
        var componentResults = new List<ResultComponentScoreDto>();
        var frozen = new List<object>();
        ResultComponentScoreDto Calculate(EvaluationSchemeComponent component, long? forStudent, bool includeContributions)
        {
            var assignments = allAssignments.Where(a => a.ComponentId == component.Id
                && a.StudentId == (component.Scope == "INDIVIDUAL" ? forStudent : null)).ToArray();
            if (assignments.Length != component.RequiredEvaluators) blockers.Add($"MISSING_EVALUATOR:{component.Id}");
            var componentScores = new List<(decimal Score, decimal Weight)>();
            var weight = forStudent.HasValue ? component.StudentWeightPercent : component.ProjectWeightPercent;
            var explicitWeights = Weights(component);
            foreach (var assignment in assignments)
            {
                var evaluation = finalized.GetValueOrDefault(assignment.Id);
                if (evaluation?.Finalization is null || evaluation.Status != "FINALIZED")
                { blockers.Add($"EVALUATION_NOT_FINALIZED:{assignment.Id}"); continue; }
                if (assignment.Scope != component.Scope || assignment.MajorId != component.MajorId || assignment.RubricId != component.RubricId
                    || assignment.PolicyVersionId != scheme.PolicyVersionId || assignment.ProjectPeriodId != scheme.ProjectPeriodId
                    || evaluation.AssignmentId != assignment.Id || evaluation.ProjectId != projectId || evaluation.EvaluatorId != assignment.EvaluatorId
                    || evaluation.RubricId != component.RubricId || evaluation.Finalization.Evidence.FinalSubmissionId != package.Id)
                { blockers.Add($"SNAPSHOT_MISMATCH:{assignment.Id}"); continue; }
                if (EvaluationScoring.FinalTotal(evaluation.Scores) != evaluation.TotalScore)
                { blockers.Add($"TOTAL_MISMATCH:{assignment.Id}"); continue; }
                var evaluatorWeight = explicitWeights?.SingleOrDefault(w => w.EvaluatorId == assignment.EvaluatorId)?.WeightPercent
                    ?? (explicitWeights is null ? 100m / component.RequiredEvaluators : 0m);
                if (evaluatorWeight <= 0) { blockers.Add($"EVALUATOR_WEIGHT_MISMATCH:{assignment.Id}"); continue; }
                var total = VersionedResultCalculator.EvaluationTotal(evaluation.Scores, calculation?.CalculationVersion ?? "LEGACY_V1");
                componentScores.Add((total, evaluatorWeight));
                if (includeContributions) contributions.Add(new(assignment.Id, evaluation.Id, evaluation.EvaluatorId, evaluation.RubricId,
                    weight * evaluatorWeight / 100m, total, evaluation.ConcurrencyToken));
                frozen.Add(new { assignment.Id, assignment.ScopeSnapshotJson, evaluation });
            }
            decimal? mean = componentScores.Count == component.RequiredEvaluators && assignments.Length == component.RequiredEvaluators
                ? explicitWeights is null ? componentScores.Average(s => s.Score)
                    : componentScores.Sum(s => s.Score * s.Weight / 100m) : null;
            return new(component.Id, component.Name, component.Scope, component.MajorId, weight, mean,
                component.RequiredEvaluators, componentScores.Count);
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
        var common = componentResults.Where(c => c.Scope == "COMMON").ToArray();
        var majorScores = componentResults.Where(c => c.Scope == "MAJOR_SPECIFIC").GroupBy(c => c.MajorId!.Value)
            .OrderBy(g => g.Key).Select(g => new ResultMajorScoreDto(g.Key, g.All(c => c.Score.HasValue)
                ? g.Sum(c => c.Score!.Value * c.WeightPercent) / g.Sum(c => c.WeightPercent) : null)).ToArray();
        var breakdown = calculation is null ? ResultSnapshotReader.Legacy(scheme.CalculationRule)
            : new ResultBreakdownDto(scheme.Name, scheme.Version, calculation.CalculationVersion, false, calculation.IsDemo,
                calculation.ApprovalStatus, final.HasValue ? "READY" : "PENDING", rawTotal,
                common.Length > 0 && common.All(c => c.Score.HasValue) ? common.Sum(c => c.Score!.Value * c.WeightPercent) / common.Sum(c => c.WeightPercent) : null,
                majorScores.Length > 0 && majorScores.All(m => m.Score.HasValue)
                    ? calculation.Formula == "DEMO_50_30_20" ? majorScores.Average(m => m.Score!.Value)
                        : componentResults.Where(c => c.Scope == "MAJOR_SPECIFIC").Sum(c => c.Score!.Value * c.WeightPercent)
                            / componentResults.Where(c => c.Scope == "MAJOR_SPECIFIC").Sum(c => c.WeightPercent) : null,
                majorScores, componentResults);
        var inputs = JsonSerializer.Serialize(new { SnapshotVersion = 2, Breakdown = breakdown, Scheme = Map(scheme), scheme.RegistrationSnapshotJson, Target = target,
            Policy = frozenPolicy is null ? null : new { frozenPolicy.Id, frozenPolicy.Version, frozenPolicy.Status, frozenPolicy.SnapshotJson },
            PackageId = package.Id, project.Status, Contributions = contributions, Evaluations = frozen, Blockers = blockers });
        var token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(inputs)));
        var preview = new ProjectResultPreviewDto(projectId, final.HasValue && blockers.Count == 0, blockers.Distinct().ToArray(), final, scheme.PassThreshold,
            final.HasValue ? final.Value >= scheme.PassThreshold ? "PASSED" : "FAILED" : null, token, contributions, breakdown);
        return new(preview, scheme, package.Id, target?.MajorId, inputs);
    }
    public Task<ProjectResultPreviewDto> PreviewAsync(long projectId, long? studentId, CancellationToken ct) => evaluations.InTransactionAsync(async () =>
    { await evaluations.LockProjectAsync(projectId, ct); return (await Check(projectId, studentId, ct)).Preview; }, ct);
    private static void Confirm(ResultCheck check, string token)
    {
        if (!check.Preview.CanPublish) throw new ConflictException(string.Join(", ", check.Preview.Blockers));
        if (!string.Equals(check.Preview.ConfirmationToken, token, StringComparison.Ordinal)) throw new ConflictException("Preview inputs changed. Reload before publication.");
    }
    public Task<ProjectResultDto> PublishProjectAsync(long projectId, string token, CancellationToken ct) => evaluations.InTransactionAsync(async () =>
    {
        await evaluations.LockProjectAsync(projectId, ct);
        var check = await Check(projectId, null, ct, publishing: true); Confirm(check, token); var now = Now;
        var saved = await results.PublishAsync(new(0, projectId, check.PackageId, check.Preview.TotalScore!.Value,
            check.Scheme.PassThreshold, check.Preview.Outcome!, check.Scheme.CalculationRule, Actor, now,
            check.Scheme.ConcurrencyToken.ToString("N"), check.Preview.Contributions, check.Scheme.Id, check.Scheme.PolicyVersionId, check.FrozenInputs,
            check.Preview.Breakdown), ct);
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
            Outcome = check.Preview.Outcome!, CalculationRule = check.Scheme.CalculationRule,
            PublishedBy = Actor, PublishedAt = Now, SnapshotJson = check.FrozenInputs };
        db.Add(row); await db.SaveChangesAsync(ct);
        db.AddRange(check.Preview.Contributions.Select(c => new StudentResultEvaluation { ResultId = row.Id, EvaluationId = c.EvaluationId }));
        await db.SaveChangesAsync(ct); await Audit("STUDENT_RESULT_PUBLISHED", "STUDENT_RESULT", row.Id, StudentDto(row), ct);
        await publisher.Publish(new WorkflowNotificationEvent(WorkflowNotificationKind.StudentResultPublished, row.Id, Actor, row.PublishedAt), ct);
        return StudentDto(row);
    }, ct);
    private static StudentResultDto StudentDto(StudentResult r) => new(r.Id, r.ProjectId, r.StudentId, r.MajorId, r.SchemeId,
        r.TotalScore, r.PassThreshold, r.Outcome, r.CalculationRule, r.PublishedBy, DateTime.SpecifyKind(r.PublishedAt, DateTimeKind.Utc), r.SnapshotJson,
        ResultSnapshotReader.Read(r.SnapshotJson, r.CalculationRule, r.StudentId, r.MajorId));
    public Task<StudentResultDto> GetStudentAsync(long projectId, long studentId, CancellationToken ct) => evaluations.InTransactionAsync(async () =>
    {
        var actor = await evaluations.GetActorAsync(Actor, ct) ?? throw new ForbiddenException();
        if (Actor != studentId) await Manage(projectId, ct);
        var row = await db.Set<StudentResult>().AsNoTracking().SingleOrDefaultAsync(r => r.ProjectId == projectId && r.StudentId == studentId, ct)
            ?? throw new NotFoundException("StudentResult", studentId);
        var canReadAuditSnapshot = Actor != studentId && actor.IsAdmin;
        if (Actor != studentId && !actor.IsAdmin)
        {
            var project = (await evaluations.GetProjectAsync(projectId, ct))!;
            canReadAuditSnapshot = AcademicAuthority.IsLead(actor, project);
            if (!canReadAuditSnapshot && ResultSnapshotReader.StudentDepartment(row.SnapshotJson, studentId) != actor.DepartmentId)
                throw new ForbiddenException();
        }
        // The publication audit snapshot contains the entire scheme roster, including other departments.
        return StudentDto(row) with { SnapshotJson = canReadAuditSnapshot ? row.SnapshotJson : "{}" };
    }, ct);
}
