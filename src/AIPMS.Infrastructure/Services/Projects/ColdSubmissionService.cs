using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Cold;
using AIPMS.Application.Features.Cold.Abstractions;
using AIPMS.Application.Features.Deliverables.Abstractions;
using AIPMS.Application.Features.Deliverables.Models;
using AIPMS.Application.Features.Deliverables.Services;
using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed partial class ColdSubmissionService(AipmsDbContext db, IEvaluationDraftRepository evaluations,
    IDeliverableRepository files, ICurrentUser current, IFileStorage storage, IAuditTrail audit,
    TimeProvider clock, ILogger<ColdSubmissionService> logger) : IColdSubmissionService
{
    private long Actor => current.IsAuthenticated && current.UserId is long id ? id : throw new UnauthorizedException();
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static ValidationException Invalid(string message) => new(new Dictionary<string, string[]> { ["cold"] = [message] });
    private static void Token(Guid actual, string? value)
    { if (!Guid.TryParse(value, out var token) || token != actual) throw new ConflictException("COLD data changed; reload before retrying."); }
    private static void Page(int page, int pageSize)
    { if (page < 1 || page > 100000 || pageSize is < 1 or > 100) throw Invalid("page must be 1..100000 and pageSize 1..100."); }
    private Task Audit(string action, string entity, long id, object data, CancellationToken ct) => audit.RecordAsync(
        new(Actor, action, entity, id, new Dictionary<string, object?> { ["snapshot"] = data }), ct);

    private sealed record Access(EvaluationActor Actor, EvaluationProject Project, RegistrationEvidence Registration,
        bool Lead, bool Primary, long? StudentMajor, IReadOnlyList<long> MentorMajors)
    {
        public bool Reads(ColdChecklistItem item) => Lead || Primary
            || Actor.IsStaff && Registration.MajorDepartmentIds!.GetValueOrDefault(item.MajorId) == Actor.DepartmentId
            || MentorMajors.Contains(item.MajorId)
            || StudentMajor == item.MajorId && (item.Scope == "MAJOR_SPECIFIC" || item.StudentId == Actor.Id);
        public bool Writes(ColdChecklistItem item) => StudentMajor == item.MajorId
            && (item.Scope == "MAJOR_SPECIFIC" || item.StudentId == Actor.Id);
    }
    private async Task<Access> Scope(long projectId, ColdPackage? package, CancellationToken ct)
    {
        var actor = await evaluations.GetActorAsync(Actor, ct) ?? throw new ForbiddenException();
        var project = await evaluations.GetProjectAsync(projectId, ct) ?? throw new NotFoundException("Project", projectId);
        if (!project.ActiveScope) throw new ForbiddenException("Active frozen academic scope is required.");
        var json = package?.RegistrationSnapshotJson ?? await db.Set<ProjectRegistrationSnapshot>().Where(s => s.ProjectId == projectId)
            .OrderByDescending(s => s.Id).Select(s => s.SnapshotJson).FirstOrDefaultAsync(ct);
        var registration = json is null ? null : ProjectAcademicScopeReader.ParseEvidence(json);
        if (registration is null || registration.Members.Count == 0) throw new ConflictException("Frozen COLD targets are unknown.");
        var lead = actor.IsStaff && actor.DepartmentId == registration.Scope.LeadDepartmentId;
        var assignments = await db.SupervisorAssignments.AsNoTracking().Where(a => a.ProjectId == projectId && a.EndedAt == null
            && a.SupervisorProfile.UserId == Actor && actor.IsLecturer).Select(a => new { a.IsPrimary, a.AssignmentType, a.MajorId }).ToArrayAsync(ct);
        var isStudent = await db.TeamMembers.AnyAsync(m => m.Team.Project != null && m.Team.Project.Id == projectId && m.UserId == Actor
            && m.LeftAt == null && m.User.Status == "ACTIVE" && m.User.AcademicProfileStatus == "VERIFIED"
            && m.User.Department != null && m.User.Department.IsActive && m.User.Department.Organization.IsActive
            && m.User.UserRoleUsers.Any(r => r.Role.Code == "STUDENT"), ct);
        var studentMajor = isStudent ? registration.Members.SingleOrDefault(m => m.UserId == Actor)?.MajorId : null;
        var primary = assignments.Any(a => a.IsPrimary && a.AssignmentType == "PRIMARY");
        var mentorMajors = assignments.Where(a => a.AssignmentType == "DISCIPLINE_MENTOR" && a.MajorId.HasValue)
            .Select(a => a.MajorId!.Value).ToArray();
        if (!lead && !primary && studentMajor is null && mentorMajors.Length == 0
            && !(actor.IsStaff && registration.DepartmentIds.Contains(actor.DepartmentId ?? 0)))
            throw new ForbiddenException("COLD access requires an active project/academic assignment.");
        return new(actor, project, registration, lead, primary, studentMajor, mentorMajors);
    }
    private async Task WriteWindow(ColdPackage package, Access access, CancellationToken ct)
    {
        if (package.Status != "OPEN" || access.Project.Status is not ("ACTIVE" or "FINAL_SUBMISSION"))
            throw new ConflictException("The COLD package or project is read-only.");
        var period = await db.ProjectPeriods.AsNoTracking().SingleAsync(p => p.Id == package.ProjectPeriodId, ct);
        if (period.Status != "ACTIVE" || Now < period.StartAt || Now >= period.EndAt || Now >= package.Deadline
            || !await db.AcademicSemesters.AnyAsync(s => s.Id == period.AcademicSemesterId && s.Status == "ACTIVE"
                && s.StartDate <= DateOnly.FromDateTime(Now) && s.EndDate >= DateOnly.FromDateTime(Now), ct))
            throw new ConflictException("COLD submission window is closed.");
    }
    private async Task<ColdPackage> Package(long projectId, CancellationToken ct) =>
        await db.Set<ColdPackage>().SingleOrDefaultAsync(p => p.ProjectId == projectId, ct) ?? throw new NotFoundException("ColdPackage", projectId);
    private async Task<(ColdChecklistItem Item, ColdPackage Package)> Item(long itemId, CancellationToken ct)
    {
        var item = await db.Set<ColdChecklistItem>().SingleOrDefaultAsync(i => i.Id == itemId, ct) ?? throw new NotFoundException("ColdChecklistItem", itemId);
        return (item, await db.Set<ColdPackage>().SingleAsync(p => p.Id == item.PackageId, ct));
    }
    private async Task<(ColdSubmission Row, ColdChecklistItem Item, ColdPackage Package)> Submission(long id, CancellationToken ct)
    {
        var row = await db.Set<ColdSubmission>().Include(s => s.Files).SingleOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("ColdSubmission", id);
        var (item, package) = await Item(row.ChecklistItemId, ct);
        return (row, item, package);
    }
    private static ColdTemplateDto TemplateDto(ColdReportTemplate row) => new(row.Id, row.MajorId, row.ProjectPeriodId,
        row.Name, row.Version, JsonSerializer.Deserialize<ColdReportInput[]>(row.ReportsJson)!);
    private static ColdSubmissionDto Dto(ColdSubmission row, ColdChecklistItem item, ColdPackage package) => new(row.Id, item.Id,
        package.ProjectId, item.ReportType, item.Scope, item.MajorId, item.StudentId, row.Version, row.Status, row.Note,
        row.CreatedBy, DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc), Utc(row.SubmittedAt), Utc(row.LockedAt), row.ConcurrencyToken.ToString("N"),
        row.Files.OrderBy(f => f.FileId).Select(f => new ColdFileDto(f.FileId, f.FileName, f.ContentType, f.SizeBytes, f.Sha256,
            $"/api/v1/cold-submissions/{row.Id}/files/{f.FileId}/download")).ToArray());
    private static DateTime? Utc(DateTime? date) => date.HasValue ? DateTime.SpecifyKind(date.Value, DateTimeKind.Utc) : null;

    private async Task TemplateScope(long majorId, long periodId, CancellationToken ct)
    {
        var actor = await evaluations.GetActorAsync(Actor, ct) ?? throw new ForbiddenException();
        if (!actor.IsStaff || !await db.Majors.AnyAsync(m => m.Id == majorId && m.IsActive && m.DepartmentId == actor.DepartmentId
            && m.Department.IsActive && m.Department.Organization.IsActive
            && db.ProjectPeriods.Any(p => p.Id == periodId && p.AcademicSemester.OrganizationId == m.Department.OrganizationId
                && (p.PeriodType == "EVALUATION" || p.PeriodType == "FINAL_SUBMISSION")), ct)) throw new ForbiddenException();
    }
    public Task<ColdTemplateDto> CreateTemplate(CreateColdTemplateRequest input, CancellationToken ct) => evaluations.InTransactionAsync(async () =>
    {
        await TemplateScope(input.MajorId, input.ProjectPeriodId, ct);
        await db.Database.SqlQuery<long>($"SELECT id AS Value FROM dbo.project_periods WITH (UPDLOCK,HOLDLOCK) WHERE id={input.ProjectPeriodId}").ToListAsync(ct);
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 200 || input.Reports is null || input.Reports.Count is < 1 or > 30
            || input.Reports.Any(r => r is null || string.IsNullOrWhiteSpace(r.Code) || r.Code.Length > 80
                || r.Code.Any(c => !char.IsAsciiLetterUpper(c) && !char.IsAsciiDigit(c) && c != '_')
                || string.IsNullOrWhiteSpace(r.Name) || r.Name.Trim().Length > 200)
            || input.Reports.Select(r => r.Code).Distinct().Count() != input.Reports.Count || !input.Reports.Any(r => r.Required))
            throw Invalid("Provide 1..30 distinct report codes (A-Z, 0-9, underscore), names and at least one required report.");
        var row = new ColdReportTemplate { MajorId = input.MajorId, ProjectPeriodId = input.ProjectPeriodId, Name = input.Name.Trim(),
            Version = (await db.Set<ColdReportTemplate>().Where(t => t.MajorId == input.MajorId && t.ProjectPeriodId == input.ProjectPeriodId)
                .MaxAsync(t => (int?)t.Version, ct) ?? 0) + 1,
            ReportsJson = JsonSerializer.Serialize(input.Reports), CreatedBy = Actor, CreatedAt = Now };
        db.Add(row); await db.SaveChangesAsync(ct); await Audit("COLD_TEMPLATE_CREATED", "COLD_TEMPLATE", row.Id, TemplateDto(row), ct);
        return TemplateDto(row);
    }, ct);
    public async Task<PagedResult<ColdTemplateDto>> Templates(long majorId, long periodId, int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize); await TemplateScope(majorId, periodId, ct);
        var query = db.Set<ColdReportTemplate>().AsNoTracking().Where(t => t.MajorId == majorId && t.ProjectPeriodId == periodId);
        var total = await query.LongCountAsync(ct);
        var rows = await query.OrderByDescending(t => t.Version).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        return new(rows.Select(TemplateDto).ToArray(), page, pageSize, total);
    }
    public Task<ColdChecklistDto> Apply(long projectId, ApplyColdChecklistRequest input, CancellationToken ct) => evaluations.InTransactionAsync(async () =>
    {
        await evaluations.LockProjectAsync(projectId, ct);
        var access = await Scope(projectId, null, ct);
        if (!access.Lead) throw new ForbiddenException("Only the frozen lead department applies the COLD checklist.");
        if (access.Project.Status is not ("ACTIVE" or "FINAL_SUBMISSION") || await db.Set<ColdPackage>().AnyAsync(p => p.ProjectId == projectId, ct))
            throw new ConflictException("A checklist is applied once to an active project; its targets and deadline are frozen.");
        var period = await db.ProjectPeriods.SingleOrDefaultAsync(p => p.Id == input.ProjectPeriodId, ct) ?? throw new NotFoundException("ProjectPeriod", input.ProjectPeriodId);
        if (period.AcademicSemesterId != access.Project.SemesterId || period.Status != "ACTIVE"
            || period.PeriodType is not ("EVALUATION" or "FINAL_SUBMISSION") || input.Deadline.UtcDateTime <= Now
            || input.Deadline.UtcDateTime > period.EndAt || input.Deadline.UtcDateTime <= period.StartAt)
            throw Invalid("The deadline must be in the project's active final-submission/evaluation period and in the future.");
        if (input.Targets is null || input.Targets.Count is < 1 or > 100 || input.Targets.Any(t => t is null)) throw Invalid("Provide 1..100 explicit template targets.");
        var ids = input.Targets.Select(t => t.TemplateId).Distinct().ToArray();
        var templates = await db.Set<ColdReportTemplate>().Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var items = new List<ColdChecklistItem>();
        foreach (var target in input.Targets)
        {
            if (!templates.TryGetValue(target.TemplateId, out var template) || template.ProjectPeriodId != period.Id
                || !access.Registration.Scope.Requirements.Any(r => r.MajorId == template.MajorId))
                throw new ConflictException("Template is outside the frozen project/period scope.");
            if (target.Scope is not ("MAJOR_SPECIFIC" or "INDIVIDUAL") || (target.Scope == "INDIVIDUAL") != target.StudentId.HasValue
                || target.StudentId.HasValue && !access.Registration.Members.Any(m => m.UserId == target.StudentId && m.MajorId == template.MajorId))
                throw Invalid("Specify a major target or a matching individual from the frozen roster.");
            foreach (var report in TemplateDto(template).Reports)
                items.Add(new() { TemplateId = template.Id, TemplateVersion = template.Version, ReportType = report.Code, Name = report.Name,
                    Required = report.Required, Scope = target.Scope, MajorId = template.MajorId, StudentId = target.StudentId });
        }
        if (items.Count > 100 || items.Select(i => (i.ReportType, i.MajorId, i.StudentId)).Distinct().Count() != items.Count)
            throw Invalid("A project checklist supports at most 100 distinct report/major/student targets.");
        var package = new ColdPackage { ProjectId = projectId, ProjectPeriodId = period.Id,
            Deadline = new DateTime(input.Deadline.UtcTicks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, DateTimeKind.Utc),
            RegistrationSnapshotJson = JsonSerializer.Serialize(access.Registration), CreatedBy = Actor, CreatedAt = Now };
        db.Add(package); await db.SaveChangesAsync(ct);
        foreach (var item in items) { item.PackageId = package.Id; db.Add(item); }
        await db.SaveChangesAsync(ct); await Audit("COLD_CHECKLIST_APPLIED", "COLD_PACKAGE", package.Id,
            new { projectId, package.Deadline, input.Targets, Items = items }, ct);
        return await ChecklistDto(package, access, ct);
    }, ct);
    public Task<ColdChecklistDto> Checklist(long projectId, CancellationToken ct) => evaluations.InTransactionAsync(async () =>
    {
        var package = await Package(projectId, ct); return await ChecklistDto(package, await Scope(projectId, package, ct), ct);
    }, ct);
    private async Task<ColdChecklistDto> ChecklistDto(ColdPackage package, Access access, CancellationToken ct)
    {
        var items = (await db.Set<ColdChecklistItem>().Where(i => i.PackageId == package.Id).OrderBy(i => i.Id).ToArrayAsync(ct)).Where(access.Reads).ToArray();
        var ids = items.Select(i => i.Id).ToArray();
        var latest = await db.Set<ColdSubmission>().Include(s => s.Files).Where(s => ids.Contains(s.ChecklistItemId)
            && !db.Set<ColdSubmission>().Any(n => n.ChecklistItemId == s.ChecklistItemId && n.Version > s.Version)).ToDictionaryAsync(s => s.ChecklistItemId, ct);
        var blockers = new List<string>();
        if (!access.Lead) blockers.Add("LEAD_DEPARTMENT_REQUIRED");
        if (package.Status == "LOCKED") blockers.Add("COLD_PACKAGE_LOCKED");
        if (access.Project.Status is not ("ACTIVE" or "FINAL_SUBMISSION")) blockers.Add("PROJECT_READ_ONLY");
        foreach (var item in items)
        {
            var submission = latest.GetValueOrDefault(item.Id);
            if (item.Required && submission is null) blockers.Add($"COLD_REPORT_REQUIRED:{item.Id}");
            if (package.Status == "OPEN" && submission is not null && (submission.Status != "SUBMITTED" || submission.Files.Count == 0)) blockers.Add($"COLD_REPORT_NOT_SUBMITTED:{item.Id}");
        }
        var windowOpen = true;
        try { await WriteWindow(package, access, ct); } catch (ConflictException) { windowOpen = false; }
        return new(package.Id, package.ProjectId, package.ProjectPeriodId, Utc(package.Deadline)!.Value, package.Status,
            package.ConcurrencyToken.ToString("N"), Utc(package.LockedAt), blockers.Count == 0, blockers,
            items.Select(i => new ColdChecklistItemDto(i.Id, i.TemplateId, i.TemplateVersion, i.ReportType, i.Name, i.Required,
                i.Scope, i.MajorId, i.StudentId, latest.TryGetValue(i.Id, out var s) ? Dto(s, i, package) : null,
                windowOpen && access.Writes(i))).ToArray());
    }
}
