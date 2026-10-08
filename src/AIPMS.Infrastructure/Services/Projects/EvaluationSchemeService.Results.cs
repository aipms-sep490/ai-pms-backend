using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Evaluations.DTOs;
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
        var scheme = await db.Set<EvaluationScheme>().Include(x => x.Components)
            .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Status == "PUBLISHED", ct)
            ?? throw new ConflictException("A published scoped evaluation scheme is required. Legacy results remain read-only.");
        var actor = (await evaluations.GetActorAsync(Actor, ct))!;
        var students = JsonSerializer.Deserialize<SchemeStudent[]>(scheme.StudentsJson)!;
        if (scheme.CalculationRule != EvaluationSchemeRules.CalculationRule)
            throw new ConflictException("This calculation rule is not supported by the current scorer.");
        var target = studentId.HasValue ? students.SingleOrDefault(x => x.StudentId == studentId)
            ?? throw new NotFoundException("StudentResult", studentId) : null;
        var components = scheme.Components.Where(c => target is null ? c.ProjectWeightPercent > 0
            : c.StudentWeightPercent > 0 && (c.Scope == "COMMON" || c.MajorId == target.MajorId)).OrderBy(c => c.Id).ToArray();
        var registration = ProjectAcademicScopeReader.ParseEvidence(scheme.RegistrationSnapshotJson)
            ?? throw new ConflictException("Frozen academic scope is unknown. Legacy results remain read-only.");
        var publicationForbidden = false;
        if (!actor.IsAdmin)
        {
            var targetDepartment = target?.DepartmentId;
            var outsideReadScope = target is not null ? targetDepartment != actor.DepartmentId
                : await db.Rubrics.AnyAsync(r => components.Select(c => c.RubricId).Contains(r.Id) && r.DepartmentId != actor.DepartmentId, ct);
            publicationForbidden = outsideReadScope || target is null && registration.DepartmentIds.Any(id => id != actor.DepartmentId);
            if (outsideReadScope || publicationForbidden && publishing)
                throw new ForbiddenException("Staff may publish their students; cross-department project results require an administrator.");
        }
        var project = (await evaluations.GetProjectAsync(projectId, ct))!;
        var package = await submissions.GetAsync(projectId, ct) ?? throw new ConflictException("Locked final package required.");
        var blockers = new List<string>();
        if (publicationForbidden) blockers.Add("ADMIN_REQUIRED_FOR_CROSS_DEPARTMENT_PUBLICATION");
        if (!project.ActiveScope) blockers.Add("ACADEMIC_SCOPE_INACTIVE");
        if (package.Items.Count == 0 || package.Items.Any(i => i.Files.Count == 0)) blockers.Add("LOCKED_PACKAGE_REQUIRED");
        if (project.Status != "FINAL_SUBMISSION" && !(studentId.HasValue && project.Status == "COMPLETED")) blockers.Add("PROJECT_NOT_FINAL_SUBMISSION");
        if (target is null ? await results.GetAsync(projectId, ct) is not null
            : await db.Set<StudentResult>().AnyAsync(r => r.ProjectId == projectId && r.StudentId == studentId, ct)) blockers.Add("ALREADY_PUBLISHED");
        var allAssignments = await db.Set<EvaluationAssignment>().AsNoTracking().Where(a => a.ProjectId == projectId && a.Status == "ACTIVE"
            && a.ComponentId != null).OrderBy(a => a.Id).ToListAsync(ct);
        var contributions = new List<ResultContributionDto>();
        var scores = new List<(decimal Weight, IReadOnlyList<decimal> Scores, int Required)>();
        var frozen = new List<object>();
        foreach (var component in components)
        {
            var assignments = allAssignments.Where(a => a.ComponentId == component.Id
                && a.StudentId == (component.Scope == "INDIVIDUAL" ? studentId : null)).ToArray();
            if (assignments.Length != component.RequiredEvaluators) blockers.Add($"MISSING_EVALUATOR:{component.Id}");
            var componentScores = new List<decimal>();
            var weight = studentId.HasValue ? component.StudentWeightPercent : component.ProjectWeightPercent;
            foreach (var assignment in assignments)
            {
                var evaluation = await evaluations.FindDraftAsync(assignment.Id, ct);
                if (evaluation?.Finalization is null || evaluation.Status != "FINALIZED")
                { blockers.Add($"EVALUATION_NOT_FINALIZED:{assignment.Id}"); continue; }
                if (assignment.Scope != component.Scope || assignment.MajorId != component.MajorId || assignment.RubricId != component.RubricId
                    || assignment.PolicyVersionId != scheme.PolicyVersionId || evaluation.Finalization.Evidence.FinalSubmissionId != package.Id)
                { blockers.Add($"SNAPSHOT_MISMATCH:{assignment.Id}"); continue; }
                var total = EvaluationScoring.FinalTotal(evaluation.Scores);
                if (total != evaluation.TotalScore) { blockers.Add($"TOTAL_MISMATCH:{assignment.Id}"); continue; }
                componentScores.Add(total);
                contributions.Add(new(assignment.Id, evaluation.Id, evaluation.EvaluatorId, evaluation.RubricId,
                    weight / component.RequiredEvaluators, total, evaluation.ConcurrencyToken));
                frozen.Add(new { assignment.Id, assignment.ScopeSnapshotJson, evaluation });
            }
            scores.Add((weight, componentScores, component.RequiredEvaluators));
        }
        // Completing the project closes scoring, so all mandatory student slots must already be finalized too.
        if (target is null)
            foreach (var component in scheme.Components.Where(c => c.StudentWeightPercent > 0))
            {
                var targets = component.Scope == "INDIVIDUAL" ? students.Where(s => s.MajorId == component.MajorId).Select(s => (long?)s.StudentId).ToArray() : [null];
                foreach (var student in targets)
                {
                    var slots = allAssignments.Where(a => a.ComponentId == component.Id && a.StudentId == student).ToArray();
                    if (slots.Length != component.RequiredEvaluators) blockers.Add($"STUDENT_ASSIGNMENTS_INCOMPLETE:{component.Id}:{student}");
                    foreach (var slot in slots)
                        if ((await evaluations.FindDraftAsync(slot.Id, ct))?.Status != "FINALIZED") blockers.Add($"STUDENT_EVALUATION_INCOMPLETE:{slot.Id}");
                }
            }
        decimal? final = blockers.Count == 0 ? EvaluationSchemeRules.Total(scores) : null;
        var inputs = JsonSerializer.Serialize(new { Scheme = Map(scheme), scheme.RegistrationSnapshotJson, target,
            Policy = await db.Set<PeriodPolicyVersion>().AsNoTracking().Where(p => p.Id == scheme.PolicyVersionId).Select(p => new { p.Id, p.Version, p.SnapshotJson }).SingleAsync(ct),
            PackageId = package.Id, project.Status, Contributions = contributions, Evaluations = frozen, Blockers = blockers });
        var token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(inputs)));
        var preview = new ProjectResultPreviewDto(projectId, final.HasValue, blockers, final, scheme.PassThreshold,
            final.HasValue ? final.Value >= scheme.PassThreshold ? "PASSED" : "FAILED" : null, token, contributions);
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
            check.Scheme.PassThreshold, check.Preview.Outcome!, EvaluationSchemeRules.CalculationRule, Actor, now,
            check.Scheme.ConcurrencyToken.ToString("N"), check.Preview.Contributions, check.Scheme.Id, check.Scheme.PolicyVersionId, check.FrozenInputs), ct);
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
            Outcome = check.Preview.Outcome!, CalculationRule = EvaluationSchemeRules.CalculationRule,
            PublishedBy = Actor, PublishedAt = Now, SnapshotJson = check.FrozenInputs };
        db.Add(row); await db.SaveChangesAsync(ct);
        db.AddRange(check.Preview.Contributions.Select(c => new StudentResultEvaluation { ResultId = row.Id, EvaluationId = c.EvaluationId }));
        await db.SaveChangesAsync(ct); await Audit("STUDENT_RESULT_PUBLISHED", "STUDENT_RESULT", row.Id, StudentDto(row), ct);
        await publisher.Publish(new WorkflowNotificationEvent(WorkflowNotificationKind.StudentResultPublished, row.Id, Actor, row.PublishedAt), ct);
        return StudentDto(row);
    }, ct);
    private static StudentResultDto StudentDto(StudentResult r) => new(r.Id, r.ProjectId, r.StudentId, r.MajorId, r.SchemeId,
        r.TotalScore, r.PassThreshold, r.Outcome, r.CalculationRule, r.PublishedBy, DateTime.SpecifyKind(r.PublishedAt, DateTimeKind.Utc), r.SnapshotJson);
    public Task<StudentResultDto> GetStudentAsync(long projectId, long studentId, CancellationToken ct) => evaluations.InTransactionAsync(async () =>
    {
        var actor = await evaluations.GetActorAsync(Actor, ct) ?? throw new ForbiddenException();
        if (Actor != studentId) await Manage(projectId, ct);
        var row = await db.Set<StudentResult>().AsNoTracking().SingleOrDefaultAsync(r => r.ProjectId == projectId && r.StudentId == studentId, ct)
            ?? throw new NotFoundException("StudentResult", studentId);
        if (Actor != studentId && !actor.IsAdmin)
        {
            var scheme = await Row(row.SchemeId, ct);
            if (!JsonSerializer.Deserialize<SchemeStudent[]>(scheme.StudentsJson)!.Any(s => s.StudentId == studentId && s.DepartmentId == actor.DepartmentId))
                throw new ForbiddenException();
        }
        // Students receive their score/contributions, not other students' roster or evaluator audit snapshots.
        return StudentDto(row) with { SnapshotJson = Actor == studentId ? "{}" : row.SnapshotJson };
    }, ct);
}
