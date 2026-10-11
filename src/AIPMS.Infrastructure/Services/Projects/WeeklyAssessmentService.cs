using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Evaluations.Services;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.WeeklyAssessments;
using AIPMS.Application.Features.WeeklyAssessments.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class WeeklyAssessmentService(AipmsDbContext db, IEvaluationDraftRepository transactions,
    IRubricRepository rubrics, ICurrentUser current, IAuditTrail audit, TimeProvider clock,
    IOptions<WeeklyAssessmentSettings> settings) : IWeeklyAssessmentService
{
    private long Actor => current.IsAuthenticated && current.UserId.HasValue ? current.UserId.Value : throw new UnauthorizedException();
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static ValidationException Invalid(string message) => new(new Dictionary<string, string[]> { ["weeklyAssessment"] = [message] });
    private sealed record Access(EvaluationProject Project, RegistrationEvidence Registration, long? PrimaryId, bool Student);
    private async Task<Access> Scope(long projectId, CancellationToken ct)
    {
        var actor = await transactions.GetActorAsync(Actor, ct) ?? throw new ForbiddenException();
        var project = await transactions.GetProjectAsync(projectId, ct) ?? throw new NotFoundException("Project", projectId);
        if (!project.ActiveScope) throw new ForbiddenException("An active frozen academic scope is required.");
        var snapshot = await db.Set<ProjectRegistrationSnapshot>().AsNoTracking().Where(s => s.ProjectId == projectId)
            .OrderByDescending(s => s.Id).Select(s => s.SnapshotJson).FirstOrDefaultAsync(ct);
        var registration = snapshot is null ? null : ProjectAcademicScopeReader.ParseEvidence(snapshot);
        if (registration is null) throw new ConflictException("Weekly assessment targets require a frozen roster.");
        var primary = await db.SupervisorAssignments.AsNoTracking().Where(a => a.ProjectId == projectId && a.IsPrimary
            && a.AssignmentType == "PRIMARY" && a.EndedAt == null && a.SupervisorProfile.UserId == Actor && actor.IsLecturer)
            .Select(a => (long?)a.Id).SingleOrDefaultAsync(ct);
        var student = registration.Members.Any(m => m.UserId == Actor) && await db.Users.AnyAsync(u => u.Id == Actor
            && u.Status == "ACTIVE" && u.UserRoleUsers.Any(r => r.Role.Code == "STUDENT"), ct);
        if (!primary.HasValue && !student) throw new ForbiddenException("Weekly assessments are visible to the current Primary and each student's own records.");
        return new(project, registration, primary, student);
    }
    private async Task<WeeklyAssessmentCalendar> Weeks(Access access, CancellationToken ct)
    {
        var semester = await db.AcademicSemesters.AsNoTracking().SingleAsync(s => s.Id == access.Project.SemesterId, ct);
        return new(semester.Id, settings.Value.TimeZoneId,
            WeeklyAssessmentWeeks.Build(semester.StartDate, semester.EndDate, settings.Value.TimeZoneId, clock.GetUtcNow()));
    }
    private async Task WriteGuard(Access access, long studentId, CancellationToken ct)
    {
        if (!access.PrimaryId.HasValue) throw new ForbiddenException("Only the current Primary can assess students.");
        if (access.Project.Status is not ("ACTIVE" or "FINAL_SUBMISSION")) throw new ConflictException("The project is read-only.");
        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(settings.Value.TimeZoneId)).DateTime);
        if (!await db.AcademicSemesters.AnyAsync(s => s.Id == access.Project.SemesterId && s.Status == "ACTIVE"
            && s.StartDate <= localToday && s.EndDate >= localToday, ct)) throw new ConflictException("The academic semester is closed.");
        if (!access.Registration.Members.Any(m => m.UserId == studentId) || !await db.TeamMembers.AnyAsync(m => m.Team.Project != null
            && m.Team.Project.Id == access.Project.Id && m.UserId == studentId && m.LeftAt == null && m.User.Status == "ACTIVE"
            && m.User.AcademicProfileStatus == "VERIFIED" && m.User.UserRoleUsers.Any(r => r.Role.Code == "STUDENT"), ct))
            throw new ForbiddenException("The student must belong to the frozen roster and active project team.");
    }
    private static void Token(WeeklyAssessment row, string? value)
    {
        if (!Guid.TryParse(value, out var supplied) || supplied != row.ConcurrencyToken) throw new ConflictException("Weekly assessment changed; reload before retrying.");
    }
    private void Started(WeeklyAssessment row)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(row.TimeZoneId)).DateTime);
        if (today < row.StartDate) throw new ConflictException("The snapshotted assessment week has not started.");
    }
    private static RubricRecord Rubric(WeeklyAssessment row) => JsonSerializer.Deserialize<RubricRecord>(row.RubricJson)!;
    private static EvaluationScoreRecord[] Scores(WeeklyAssessment row)
    {
        var rubric = Rubric(row);
        var parents = rubric.Criteria.Where(c => c.ParentId.HasValue).Select(c => c.ParentId!.Value).ToHashSet();
        return RubricHierarchy.WithEffectiveWeights(rubric.Criteria).Where(c => !parents.Contains(c.Id)).OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
            .Select(c => new EvaluationScoreRecord(c.Id, c.Name, c.Description, c.EffectiveWeightPercent, c.MaxScore!.Value,
                c.SortOrder, c.IsRequired, row.Scores.SingleOrDefault(s => s.RubricCriterionId == c.Id)?.Score,
                row.Scores.SingleOrDefault(s => s.RubricCriterionId == c.Id)?.Comments)).ToArray();
    }
    private async Task<WeeklyAssessmentDto> Dto(WeeklyAssessment row, Access access, CancellationToken ct, bool? writeContext = null)
    {
        var writable = access.PrimaryId == row.PrimaryAssignmentId && row.CreatedBy == Actor && row.Status == "DRAFT";
        if (writeContext.HasValue) writable &= writeContext.Value;
        else if (writable) { try { await WriteGuard(access, row.StudentId, ct); } catch (ConflictException) { writable = false; } catch (ForbiddenException) { writable = false; } }
        if (writable) { try { Started(row); } catch (ConflictException) { writable = false; } }
        var rubric = Rubric(row); var scores = Scores(row); var preview = EvaluationScoring.Preview(scores, 10);
        return new(row.Id, row.ProjectId, row.StudentId, row.MajorId, row.AcademicSemesterId, row.WeekNumber, row.StartDate, row.EndDate,
            row.TimeZoneId, row.Version, row.Status, rubric.Id, rubric.Name, rubric.Version, scores, row.TotalScore, row.Comments,
            row.RevisionReason, row.ConcurrencyToken.ToString("N"), row.CreatedBy, row.PrimaryAssignmentId,
            DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc), row.SubmittedAt.HasValue ? DateTime.SpecifyKind(row.SubmittedAt.Value, DateTimeKind.Utc) : null,
            writable, writable && preview.Total.HasValue);
    }
    private Task Audit(string action, WeeklyAssessment row, CancellationToken ct) => audit.RecordAsync(new(Actor, action, "WEEKLY_ASSESSMENT", row.Id,
        new Dictionary<string, object?> { ["projectId"] = row.ProjectId, ["studentId"] = row.StudentId, ["version"] = row.Version,
            ["primaryAssignmentId"] = row.PrimaryAssignmentId, ["week"] = row.WeekNumber, ["isDemo"] = true,
            ["revisionReason"] = row.RevisionReason, ["token"] = row.ConcurrencyToken.ToString("N") }), ct);
    public Task<WeeklyAssessmentCalendar> Calendar(long projectId, CancellationToken ct) => transactions.InTransactionAsync(async () => await Weeks(await Scope(projectId, ct), ct), ct);
    public async Task<PagedResult<RubricRecord>> AvailableRubrics(long projectId, int page, int pageSize, CancellationToken ct)
    {
        if (page < 1 || page > 100000 || pageSize is < 1 or > 100) throw Invalid("Invalid bounded paging.");
        var access = await Scope(projectId, ct);
        if (!access.PrimaryId.HasValue) throw new ForbiddenException();
        return await rubrics.ListAsync(new(Actor, false, access.Registration.Scope.LeadDepartmentId),
            new(access.Registration.Scope.LeadDepartmentId, access.Project.SemesterId, "PUBLISHED", null, page, pageSize), ct);
    }

    public Task<WeeklyAssessmentDto> Save(long projectId, SaveWeeklyAssessmentRequest input, CancellationToken ct) => transactions.InTransactionAsync(async () =>
    {
        await transactions.LockProjectAsync(projectId, ct);
        var access = await Scope(projectId, ct); await WriteGuard(access, input.StudentId, ct);
        if (input.ExpectedLatestVersion < 0 || input.Comments?.Length > 2000 || input.RevisionReason?.Length > 1000 || input.Scores is null
            || input.Scores.Count > 200 || input.Scores.Any(s => s is null || s.Comments?.Length > 2000)
            || input.Scores.Select(s => s.RubricCriterionId).Distinct().Count() != input.Scores.Count) throw Invalid("Invalid version, scores or comment length.");
        var latest = await db.Set<WeeklyAssessment>().Include(w => w.Scores).Where(w => w.ProjectId == projectId
            && w.StudentId == input.StudentId && w.WeekNumber == input.WeekNumber).OrderByDescending(w => w.Version).FirstOrDefaultAsync(ct);
        if ((latest?.Version ?? 0) != input.ExpectedLatestVersion) throw new ConflictException("A newer weekly version exists.");
        WeeklyAssessment row;
        if (latest?.Status == "DRAFT" && latest.PrimaryAssignmentId == access.PrimaryId && latest.CreatedBy == Actor)
        {
            Token(latest, input.ConcurrencyToken); row = latest;
            Started(row);
            if (input.RubricId != row.RubricId) throw new ConflictException("The draft rubric snapshot cannot change.");
        }
        else
        {
            if (input.ConcurrencyToken is not null || latest is not null && string.IsNullOrWhiteSpace(input.RevisionReason))
                throw new ConflictException("A new version requires a reason and no old draft token.");
            if (latest is not null && input.RubricId != latest.RubricId) throw new ConflictException("Revisions retain the historical rubric.");
            var calendar = await Weeks(access, ct);
            var week = calendar.Weeks.SingleOrDefault(w => w.WeekNumber == input.WeekNumber);
            if (week is null || !week.HasStarted) throw Invalid("Select an elapsed or current semester week; future weeks cannot be assessed.");
            RubricRecord rubric;
            if (latest is null)
            {
                rubric = await rubrics.GetAsync(input.RubricId, new(Actor, true, null), true, ct) ?? throw new NotFoundException("Rubric", input.RubricId);
                if (rubric.Status != "PUBLISHED" || rubric.AcademicSemesterId != access.Project.SemesterId
                    || rubric.DepartmentId != access.Registration.Scope.LeadDepartmentId)
                    throw new ConflictException("A published rubric from the semester and frozen lead department is required.");
                RubricRules.EnsurePublishable(rubric.Criteria);
            }
            else rubric = Rubric(latest);
            if (latest?.Status == "DRAFT")
            {
                latest.Status = "SUPERSEDED"; latest.ConcurrencyToken = Guid.NewGuid(); latest.UpdatedAt = Now;
                await db.SaveChangesAsync(ct); await Audit("WEEKLY_DRAFT_SUPERSEDED", latest, ct);
            }
            row = new() { ProjectId = projectId, StudentId = input.StudentId,
                MajorId = latest?.MajorId ?? access.Registration.Members.Single(m => m.UserId == input.StudentId).MajorId,
                AcademicSemesterId = access.Project.SemesterId, WeekNumber = input.WeekNumber,
                StartDate = latest?.StartDate ?? week.StartDate, EndDate = latest?.EndDate ?? week.EndDate,
                TimeZoneId = latest?.TimeZoneId ?? calendar.TimeZoneId, Version = (latest?.Version ?? 0) + 1,
                RubricId = rubric.Id, RubricJson = JsonSerializer.Serialize(rubric), CreatedBy = Actor,
                PrimaryAssignmentId = access.PrimaryId!.Value, CreatedAt = Now, RevisionReason = input.RevisionReason?.Trim() };
            db.Add(row);
        }
        var leaves = Scores(row).Select(s => s.RubricCriterionId).ToHashSet();
        if (input.Scores.Any(s => !leaves.Contains(s.RubricCriterionId))) throw Invalid("Scores must target leaf criteria from the frozen rubric.");
        var inputs = input.Scores.ToDictionary(s => s.RubricCriterionId);
        foreach (var old in row.Scores.ToArray())
            if (!inputs.ContainsKey(old.RubricCriterionId)) { db.Remove(old); row.Scores.Remove(old); }
        foreach (var inputScore in input.Scores)
        {
            var score = row.Scores.SingleOrDefault(s => s.RubricCriterionId == inputScore.RubricCriterionId);
            if (score is null) { score = new() { RubricCriterionId = inputScore.RubricCriterionId }; row.Scores.Add(score); }
            score.Score = inputScore.Score; score.Comments = inputScore.Comments?.Trim();
        }
        row.TotalScore = EvaluationScoring.Preview(Scores(row), 10).Total;
        row.Comments = input.Comments?.Trim(); row.ConcurrencyToken = Guid.NewGuid(); row.UpdatedAt = Now;
        await db.SaveChangesAsync(ct); await Audit("WEEKLY_ASSESSMENT_DRAFT_SAVED", row, ct);
        await WriteGuard(access, row.StudentId, ct); Started(row);
        return await Dto(row, access, ct);
    }, ct);

    public Task<WeeklyAssessmentDto> Submit(long id, string token, CancellationToken ct) => transactions.InTransactionAsync(async () =>
    {
        var projectId = await db.Set<WeeklyAssessment>().Where(w => w.Id == id).Select(w => (long?)w.ProjectId).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("WeeklyAssessment", id);
        await transactions.LockProjectAsync(projectId, ct);
        var row = await db.Set<WeeklyAssessment>().Include(w => w.Scores).SingleAsync(w => w.Id == id, ct);
        var access = await Scope(projectId, ct); await WriteGuard(access, row.StudentId, ct); Token(row, token);
        Started(row);
        if (row.Status != "DRAFT" || row.CreatedBy != Actor || row.PrimaryAssignmentId != access.PrimaryId)
            throw new ConflictException("Only the current Primary's own draft may be submitted.");
        row.TotalScore = EvaluationScoring.FinalTotal(Scores(row)); row.Status = "SUBMITTED"; row.SubmittedAt = Now;
        row.UpdatedAt = Now; row.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct); await Audit("WEEKLY_ASSESSMENT_SUBMITTED", row, ct);
        await WriteGuard(access, row.StudentId, ct);
        return await Dto(row, access, ct);
    }, ct);

    public Task<WeeklyAssessmentDto> Get(long id, CancellationToken ct) => transactions.InTransactionAsync(async () =>
    {
        var row = await db.Set<WeeklyAssessment>().AsNoTracking().Include(w => w.Scores).SingleOrDefaultAsync(w => w.Id == id, ct)
            ?? throw new NotFoundException("WeeklyAssessment", id);
        var access = await Scope(row.ProjectId, ct);
        if (!access.PrimaryId.HasValue && (row.StudentId != Actor || row.Status != "SUBMITTED")) throw new ForbiddenException();
        return await Dto(row, access, ct);
    }, ct);
    public Task<PagedResult<WeeklyAssessmentDto>> History(long projectId, long? studentId, int? week, int page, int pageSize, CancellationToken ct) => transactions.InTransactionAsync(async () =>
    {
        if (page < 1 || page > 100000 || pageSize is < 1 or > 100 || week is < 1 or > 105) throw Invalid("Invalid bounded paging or week.");
        var access = await Scope(projectId, ct);
        if (!access.PrimaryId.HasValue && studentId.HasValue && studentId != Actor) throw new ForbiddenException();
        var query = db.Set<WeeklyAssessment>().AsNoTracking().Where(w => w.ProjectId == projectId
            && (!studentId.HasValue || w.StudentId == studentId) && (!week.HasValue || w.WeekNumber == week));
        if (!access.PrimaryId.HasValue) query = query.Where(w => w.StudentId == Actor && w.Status == "SUBMITTED");
        var count = await query.LongCountAsync(ct);
        var rows = await query.Include(w => w.Scores).OrderByDescending(w => w.WeekNumber).ThenBy(w => w.StudentId).ThenByDescending(w => w.Version)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        var dtos = new List<WeeklyAssessmentDto>();
        var eligibleStudents = new HashSet<long>();
        if (access.PrimaryId.HasValue && access.Project.Status is "ACTIVE" or "FINAL_SUBMISSION")
        {
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(settings.Value.TimeZoneId)).DateTime);
            if (await db.AcademicSemesters.AnyAsync(s => s.Id == access.Project.SemesterId && s.Status == "ACTIVE" && s.StartDate <= today && s.EndDate >= today, ct))
            {
                var students = rows.Select(r => r.StudentId).Intersect(access.Registration.Members.Select(m => m.UserId)).ToArray();
                eligibleStudents = (await db.TeamMembers.Where(m => m.Team.Project != null && m.Team.Project.Id == projectId
                    && students.Contains(m.UserId) && m.LeftAt == null && m.User.Status == "ACTIVE" && m.User.AcademicProfileStatus == "VERIFIED"
                    && m.User.UserRoleUsers.Any(r => r.Role.Code == "STUDENT")).Select(m => m.UserId).ToArrayAsync(ct)).ToHashSet();
            }
        }
        foreach (var row in rows) dtos.Add(await Dto(row, access, ct, eligibleStudents.Contains(row.StudentId)));
        return new PagedResult<WeeklyAssessmentDto>(dtos, page, pageSize, count);
    }, ct);
}
