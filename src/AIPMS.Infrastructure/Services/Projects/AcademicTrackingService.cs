using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.AcademicTracking;
using AIPMS.Application.Features.AcademicTracking.Abstractions;
using AIPMS.Application.Features.Deliverables.Services;
using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed partial class AcademicTrackingService(AipmsDbContext db, IEvaluationDraftRepository transactions,
    ICurrentUser current, IAuditTrail audit, TimeProvider clock, DeliverableWorkflow fileWorkflow) : IAcademicTrackingService
{
    private long Actor => current.IsAuthenticated && current.UserId.HasValue ? current.UserId.Value : throw new UnauthorizedException();
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static ValidationException Invalid(string text) => new(new Dictionary<string, string[]> { ["academicTracking"] = [text] });
    private sealed record Assignment(long Id, string Type, long? MajorId);
    private sealed record Access(EvaluationProject Project, RegistrationEvidence Registration, EvaluationActor Actor,
        Assignment[] Assignments, long? StudentMajor)
    {
        public bool Lead => Actor.IsStaff && Actor.DepartmentId == Project.LeadDepartmentId;
        public bool Primary => Assignments.Any(a => a.Type == "PRIMARY");
        public long? ReviewAssignment(long? major) => Assignments.FirstOrDefault(a => a.Type == "PRIMARY"
            || major.HasValue && a.Type == "DISCIPLINE_MENTOR" && a.MajorId == major)?.Id;
        public bool KnownMajor(long? major) => !major.HasValue || Registration.Scope.Requirements.Any(r => r.MajorId == major);
        public bool CanRead(long? major) => KnownMajor(major) && (Lead || Primary || major.HasValue && (StudentMajor == major
            || Assignments.Any(a => a.Type == "DISCIPLINE_MENTOR" && a.MajorId == major)
            || Actor.IsStaff && Registration.MajorDepartmentIds!.GetValueOrDefault(major.Value) == Actor.DepartmentId)
            || major is null && StudentMajor.HasValue);
        public bool CanManage(long? major) => KnownMajor(major) && (Lead || ReviewAssignment(major).HasValue);
        public bool Writable => Project.Status is "ACTIVE" or "FINAL_SUBMISSION";
    }
    private async Task<Access> Scope(long projectId, CancellationToken ct)
    {
        var actor = await transactions.GetActorAsync(Actor, ct) ?? throw new ForbiddenException();
        var project = await transactions.GetProjectAsync(projectId, ct) ?? throw new NotFoundException("Project", projectId);
        if (!project.ActiveScope) throw new ForbiddenException("An active frozen academic scope is required.");
        var json = await db.Set<ProjectRegistrationSnapshot>().Where(s => s.ProjectId == projectId).OrderByDescending(s => s.Id)
            .Select(s => s.SnapshotJson).FirstOrDefaultAsync(ct);
        var registration = json is null ? null : ProjectAcademicScopeReader.ParseEvidence(json);
        if (registration is null) throw new ConflictException("Academic scope is unknown.");
        var assignments = actor.IsLecturer ? await db.SupervisorAssignments.Where(a => a.ProjectId == projectId && a.EndedAt == null
            && a.SupervisorProfile.UserId == Actor && (a.AssignmentType == "PRIMARY" && a.IsPrimary || a.AssignmentType == "DISCIPLINE_MENTOR"))
            .Select(a => new Assignment(a.Id, a.AssignmentType, a.MajorId)).ToArrayAsync(ct) : [];
        long? studentMajor = null;
        if (await db.TeamMembers.AnyAsync(m => m.Team.Project != null && m.Team.Project.Id == projectId && m.UserId == Actor
            && m.LeftAt == null && m.User.UserRoleUsers.Any(r => r.Role.Code == "STUDENT"), ct))
            studentMajor = registration.Members.SingleOrDefault(m => m.UserId == Actor)?.MajorId;
        var access = new Access(project, registration, actor, assignments, studentMajor);
        if (!access.Lead && assignments.Length == 0 && !studentMajor.HasValue
            && !(actor.IsStaff && project.DepartmentIds.Contains(actor.DepartmentId ?? 0))) throw new ForbiddenException();
        return access;
    }
    private static void Page(int page, int size) { if (page is < 1 or > 100000 || size is < 1 or > 100) throw Invalid("Invalid bounded pagination."); }
    private static void CheckToken(Guid currentToken, string token)
    { if (!Guid.TryParse(token, out var value) || currentToken != value) throw new ConflictException("The resource changed; reload before retrying."); }
    private static void Write(Access access, long? major)
    {
        if (!access.CanManage(major)) throw new ForbiddenException();
        if (!access.Writable) throw new ConflictException("The project is read-only.");
        if (major.HasValue && !access.Registration.Scope.Requirements.Any(r => r.MajorId == major)) throw Invalid("The target major is outside frozen scope.");
    }
    private Task Audit(string action, string type, long id, object data, CancellationToken ct) => audit.RecordAsync(new(Actor, action, type, id,
        new Dictionary<string, object?> { ["detail"] = data, ["trackingOnly"] = true }), ct);
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private async Task<M.ProjectEvidence> Evidence(long id, CancellationToken ct) => await db.ProjectEvidence.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, ct)
        ?? throw new NotFoundException("ProjectEvidence", id);
    private sealed record SourceSnapshot(string Json, string Hash);
    private async Task<SourceSnapshot> Capture(M.ProjectEvidence evidence, long? versionId, CancellationToken ct)
    {
        object content;
        IQueryable<M.File> files = db.Files.Where(f => false);
        if (evidence.SourceType != "DELIVERABLE" && versionId.HasValue) throw Invalid("SourceVersionId only applies to a deliverable version.");
        switch (evidence.SourceType)
        {
            case "TASK":
                var task = await db.Tasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == evidence.SourceId && t.Milestone.ProjectId == evidence.ProjectId, ct)
                    ?? throw new NotFoundException("Evidence source", evidence.SourceId);
                if (evidence.MajorId.HasValue && !await db.TaskDisciplines.AnyAsync(d => d.TaskId == task.Id && d.MajorId == evidence.MajorId, ct))
                    throw new ConflictException("Task discipline no longer matches evidence scope.");
                content = new { task.Id, task.Title, task.Description, task.Status, task.ConcurrencyToken, task.UpdatedAt };
                files = db.Files.Where(f => f.TaskId == task.Id); break;
            case "DELIVERABLE":
                var version = await db.DeliverableVersions.AsNoTracking().Include(v => v.Deliverable).SingleOrDefaultAsync(v => v.Id == versionId
                    && v.DeliverableId == evidence.SourceId && v.Deliverable.ProjectId == evidence.ProjectId, ct)
                    ?? throw new NotFoundException("Exact deliverable version", versionId ?? 0);
                if (evidence.MajorId.HasValue && version.Deliverable.TargetMajorId != evidence.MajorId)
                    throw new ConflictException("Explicit deliverable target does not match evidence major.");
                content = new { version.Id, version.VersionNumber, version.Status, version.SubmissionNote, version.UpdatedAt,
                    version.Deliverable.Title, version.Deliverable.EvidenceScope, version.Deliverable.TargetMajorId, version.Deliverable.TargetStudentId };
                files = db.Files.Where(f => f.DeliverableVersionId == version.Id); break;
            case "MEETING":
                if (evidence.MajorId.HasValue) throw new ConflictException("Meeting evidence has no authoritative major target; review as project-wide evidence.");
                var meeting = await db.Meetings.AsNoTracking().SingleOrDefaultAsync(m => m.Id == evidence.SourceId && m.ProjectId == evidence.ProjectId, ct)
                    ?? throw new NotFoundException("Evidence source", evidence.SourceId);
                content = new { meeting.Id, meeting.Title, meeting.Agenda, meeting.MeetingNotes, meeting.Status, meeting.ConcurrencyToken };
                files = db.Files.Where(f => f.MeetingId == meeting.Id); break;
            case "PROGRESS_REPORT":
                if (evidence.MajorId.HasValue) throw new ConflictException("Progress report evidence has no authoritative major target; review as project-wide evidence.");
                var report = await db.ProgressReports.AsNoTracking().SingleOrDefaultAsync(r => r.Id == evidence.SourceId && r.ProjectId == evidence.ProjectId, ct)
                    ?? throw new NotFoundException("Evidence source", evidence.SourceId);
                content = new { report.Id, report.Summary, report.CompletedWork, report.PlannedWork, report.IssuesAndRisks, report.Status, report.ConcurrencyToken };
                files = db.Files.Where(f => f.ProgressReportId == report.Id); break;
            case "FILE":
                await fileWorkflow.FileAsync(evidence.SourceId, ct);
                var belongs = await db.Files.AnyAsync(f => f.Id == evidence.SourceId && (f.Task != null && f.Task.Milestone.ProjectId == evidence.ProjectId
                    || f.DeliverableVersion != null && f.DeliverableVersion.Deliverable.ProjectId == evidence.ProjectId
                    || f.Meeting != null && f.Meeting.ProjectId == evidence.ProjectId || f.ProgressReport != null && f.ProgressReport.ProjectId == evidence.ProjectId
                    || f.SupervisorFeedback != null && f.SupervisorFeedback.ProjectId == evidence.ProjectId), ct);
                if (!belongs) throw new NotFoundException("Evidence file", evidence.SourceId);
                if (evidence.MajorId.HasValue && !await db.Files.AnyAsync(f => f.Id == evidence.SourceId && (
                    f.DeliverableVersion != null && f.DeliverableVersion.Deliverable.TargetMajorId == evidence.MajorId
                    || f.TaskId.HasValue && db.TaskDisciplines.Any(d => d.TaskId == f.TaskId && d.MajorId == evidence.MajorId)), ct))
                    throw new ConflictException("File requires an explicit deliverable target or task discipline matching evidence major.");
                content = new { FileId = evidence.SourceId }; files = db.Files.Where(f => f.Id == evidence.SourceId); break;
            default: throw new ConflictException("Legacy evidence source cannot be reviewed.");
        }
        // Private snapshot preserves the exact source and storage references; DTOs expose only its hash.
        var snapshotFiles = await files.AsNoTracking().OrderBy(f => f.Id).Select(f => new {
            f.Id, f.DeliverableVersionId, f.OriginalFileName, f.StoragePath, f.ChecksumSha256, f.FileSizeBytes, f.MimeType, f.UpdatedAt }).Take(101).ToArrayAsync(ct);
        if (snapshotFiles.Length > 100) throw new ConflictException("Evidence exceeds the bounded review manifest.");
        var json = JsonSerializer.Serialize(new { evidence.Id, evidence.ProjectId, evidence.MajorId, evidence.SourceType, evidence.SourceId,
            evidence.Notes, SourceVersionId = versionId, Content = content, Files = snapshotFiles });
        return new(json, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant());
    }
    private static EvidenceReviewDto ReviewDto(ProjectEvidenceReview row) => new(row.Id, row.EvidenceId, row.SourceVersionId,
        row.SourceVersion, row.ReviewerId, row.Decision, row.Reason, Utc(row.ReviewedAt));
    public Task<EvidenceReviewTarget> ReviewTarget(long evidenceId, long? sourceVersionId, CancellationToken ct) => transactions.InTransactionAsync(async () =>
    {
        var evidence = await Evidence(evidenceId, ct); var access = await Scope(evidence.ProjectId, ct);
        if (!access.CanRead(evidence.MajorId) || access.StudentMajor.HasValue && !access.Primary && !access.Actor.IsStaff) throw new ForbiddenException();
        var source = await Capture(evidence, sourceVersionId, ct);
        var latest = await db.Set<ProjectEvidenceReview>().Where(r => r.EvidenceId == evidenceId).MaxAsync(r => (long?)r.Id, ct) ?? 0;
        var canReview = access.CanManage(evidence.MajorId) && access.ReviewAssignment(evidence.MajorId).HasValue && access.Writable;
        return new EvidenceReviewTarget(evidenceId, sourceVersionId, source.Hash, latest, canReview,
            canReview ? null : !access.Writable ? "PROJECT_READ_ONLY" : "SCOPED_REVIEWER_REQUIRED");
    }, ct);
    public Task<EvidenceReviewDto> Review(long evidenceId, ReviewEvidenceRequest input, CancellationToken ct) => transactions.InTransactionAsync(async () =>
    {
        var evidence = await Evidence(evidenceId, ct); await transactions.LockProjectAsync(evidence.ProjectId, ct);
        var access = await Scope(evidence.ProjectId, ct); Write(access, evidence.MajorId);
        var assignment = access.ReviewAssignment(evidence.MajorId) ?? throw new ForbiddenException("Only an assigned Primary or scoped Mentor can review evidence.");
        if (input.Decision is not ("ACCEPTED" or "CHANGES_REQUESTED" or "REJECTED") || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 2000)
            throw Invalid("Decision and a nonempty reason (maximum 2000 characters) are required.");
        var latest = await db.Set<ProjectEvidenceReview>().Where(r => r.EvidenceId == evidenceId).MaxAsync(r => (long?)r.Id, ct) ?? 0;
        var source = await Capture(evidence, input.SourceVersionId, ct);
        if (latest != input.ExpectedLatestReviewId || source.Hash != input.ExpectedSourceVersion) throw new ConflictException("The source or review history changed; reload.");
        var row = new ProjectEvidenceReview { EvidenceId = evidenceId, SourceVersionId = input.SourceVersionId, SourceVersion = source.Hash,
            SourceSnapshotJson = source.Json, ReviewerId = Actor, SupervisorAssignmentId = assignment, Decision = input.Decision, Reason = input.Reason.Trim(), ReviewedAt = Now };
        db.Add(row); await db.SaveChangesAsync(ct); await Audit("EVIDENCE_REVIEW_RECORDED", "PROJECT_EVIDENCE", evidenceId,
            new { reviewId = row.Id, sourceVersion = source.Hash, assignmentId = assignment, input.Decision }, ct);
        return ReviewDto(row);
    }, ct);
    public Task<PagedResult<EvidenceReviewDto>> Reviews(long evidenceId, int page, int pageSize, CancellationToken ct) => transactions.InTransactionAsync(async () =>
    {
        Page(page, pageSize); var evidence = await Evidence(evidenceId, ct); var access = await Scope(evidence.ProjectId, ct);
        if (!access.CanRead(evidence.MajorId) || access.StudentMajor.HasValue && !access.Primary && !access.Actor.IsStaff) throw new ForbiddenException();
        var query = db.Set<ProjectEvidenceReview>().AsNoTracking().Where(r => r.EvidenceId == evidenceId);
        var count = await query.LongCountAsync(ct);
        var rows = await query.OrderByDescending(r => r.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        return new PagedResult<EvidenceReviewDto>(rows.Select(ReviewDto).ToArray(), page, pageSize, count);
    }, ct);
}
