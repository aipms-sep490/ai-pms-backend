using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.AcademicTracking;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class AcademicTrackingEndpointTests(EvaluationDraftDatabaseFixture database) : IClassFixture<EvaluationDraftDatabaseFixture>
{
    private static DateTime Now => EvaluationDraftDatabaseFixture.Now;
    private static async Task<T> Body<T>(HttpResponseMessage response)
    { Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<T>())!; }
    private static string Project(long id) => $"/api/v1/projects/{id}";
    private static string Evidence(long id) => $"/api/v1/project-evidence/{id}";
    private async Task<(EvaluationScenario S, long Primary, long Major, long Evidence, long Task)> Prepare()
    {
        var s = await database.Seed(false); await using var db = database.CreateContext();
        var assignment = await db.SupervisorAssignments.Include(a => a.SupervisorProfile).SingleAsync(a => a.ProjectId == s.ProjectId);
        var major = await db.ProjectMajors.Where(m => m.ProjectId == s.ProjectId).Select(m => m.MajorId).SingleAsync();
        var task = new M.Task { Title = "Synthetic task", Status = "TODO", CreatedBy = s.Scope.Users.Student,
            Milestone = new() { ProjectId = s.ProjectId, Title = "Synthetic milestone", Status = "PLANNED", CreatedBy = s.Scope.Users.Student } };
        db.Tasks.Add(task); await db.SaveChangesAsync();
        db.TaskDisciplines.Add(new() { TaskId = task.Id, MajorId = major, Role = "PRIMARY", CreatedBy = s.Scope.Users.Student, CreatedAt = Now });
        var evidence = new M.ProjectEvidence { ProjectId = s.ProjectId, MajorId = major, SourceType = "TASK", SourceId = task.Id,
            TaskId = task.Id, VerificationStatus = "PENDING", SubmittedBy = s.Scope.Users.Student, SubmittedAt = Now };
        db.ProjectEvidence.Add(evidence); await db.SaveChangesAsync();
        return (s, assignment.SupervisorProfile.UserId, major, evidence.Id, task.Id);
    }
    private static ReviewEvidenceRequest Review(EvidenceReviewTarget target, string decision = "ACCEPTED") => new(target.SourceVersionId,
        target.SourceVersion, target.LatestReviewId, decision, "Synthetic reason");

    [Fact]
    public async Task Exact_source_review_is_append_only_stale_safe_and_checkpoint_never_changes_project_state()
    {
        using var app = new TrackingFactory(database); var x = await Prepare(); using var primary = app.CreateAuthenticatedClient(x.Primary);
        var target = await Body<EvidenceReviewTarget>(await primary.GetAsync(Evidence(x.Evidence) + "/review-target"));
        Assert.True(target.CanReview);
        await using (var db = database.CreateContext()) { var task = (await db.Tasks.FindAsync(x.Task))!; task.Description = "New version"; task.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Conflict, (await primary.PostAsJsonAsync(Evidence(x.Evidence) + "/reviews", Review(target))).StatusCode);
        target = await Body<EvidenceReviewTarget>(await primary.GetAsync(Evidence(x.Evidence) + "/review-target"));
        var review = await Body<EvidenceReviewDto>(await primary.PostAsJsonAsync(Evidence(x.Evidence) + "/reviews", Review(target)));
        Assert.Equal(target.SourceVersion, review.SourceVersion);
        Assert.Equal(HttpStatusCode.Conflict, (await primary.PostAsJsonAsync(Evidence(x.Evidence) + "/reviews", Review(target))).StatusCode);
        var checkpoint = await Body<CheckpointDto>(await primary.PostAsJsonAsync(Project(x.S.ProjectId) + "/checkpoints", new SaveCheckpointRequest("Demo gate", x.Major, new(Now.AddDays(-1)))));
        Assert.True(checkpoint.IsOverdue); Assert.True(checkpoint.TrackingOnly); Assert.False(checkpoint.BlocksAcademicWorkflow);
        var decision = new DecideCheckpointRequest(checkpoint.ConcurrencyToken, "NOT_MET", "Tracking signal only", [new(x.Evidence, review.Id)]);
        checkpoint = await Body<CheckpointDto>(await primary.PostAsJsonAsync($"/api/v1/checkpoints/{checkpoint.Id}/decisions", decision));
        Assert.Single(checkpoint.Decisions); Assert.True(checkpoint.IsOverdue);
        Assert.Equal(HttpStatusCode.Conflict, (await primary.PostAsJsonAsync($"/api/v1/checkpoints/{checkpoint.Id}/decisions", decision)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await primary.PutAsJsonAsync(Project(x.S.ProjectId) + $"/checkpoints/{checkpoint.Id}", new SaveCheckpointRequest("Rewrite", x.Major, new(Now), checkpoint.ConcurrencyToken))).StatusCode);
        await using var verify = database.CreateContext();
        Assert.Equal("FINAL_SUBMISSION", (await verify.Projects.FindAsync(x.S.ProjectId))!.Status);
        Assert.Equal("PENDING", (await verify.ProjectEvidence.FindAsync(x.Evidence))!.VerificationStatus);
        Assert.Single(await verify.Set<ProjectEvidenceReview>().Where(r => r.EvidenceId == x.Evidence).ToArrayAsync());
        Assert.True(await verify.AuditLogs.AnyAsync(a => a.Action == "CHECKPOINT_DECISION_RECORDED" && a.EntityId == checkpoint.Id.ToString()));
        var history = await Body<PagedResult<EvidenceReviewDto>>(await primary.GetAsync(Evidence(x.Evidence) + "/reviews"));
        Assert.Equal(review.SourceVersion, history.Items.Single().SourceVersion);
        Assert.DoesNotContain("sourceSnapshotJson", JsonSerializer.Serialize(history), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Scoped_mentor_review_ended_assignment_admin_and_archived_write_denials()
    {
        using var app = new TrackingFactory(database); var x = await Prepare(); using var primary = app.CreateAuthenticatedClient(x.Primary);
        long mentorAssignment;
        await using (var db = database.CreateContext())
        {
            var profile = new M.SupervisorProfile { UserId = x.S.Scope.Users.NewLecturer };
            var request = new M.SupervisorRequest { ProjectId = x.S.ProjectId, SupervisorProfile = profile, RequestedBy = x.S.Scope.Users.Student,
                Status = "ACCEPTED", RequestedAt = Now.AddDays(-1), RespondedAt = Now };
            var assignment = new M.SupervisorAssignment { ProjectId = x.S.ProjectId, SupervisorRequest = request, SupervisorProfile = profile,
                AssignmentType = "DISCIPLINE_MENTOR", MajorId = x.Major, IsPrimary = false, AssignedAt = Now };
            db.Add(assignment); await db.SaveChangesAsync(); mentorAssignment = assignment.Id;
        }
        using var mentor = app.CreateAuthenticatedClient(x.S.Scope.Users.NewLecturer);
        var target = await Body<EvidenceReviewTarget>(await mentor.GetAsync(Evidence(x.Evidence) + "/review-target"));
        await Body<EvidenceReviewDto>(await mentor.PostAsJsonAsync(Evidence(x.Evidence) + "/reviews", Review(target)));
        Assert.Equal(HttpStatusCode.Forbidden, (await mentor.PostAsJsonAsync(Project(x.S.ProjectId) + "/checkpoints", new SaveCheckpointRequest("Common", null, new(Now)))).StatusCode);
        await Body<CheckpointDto>(await mentor.PostAsJsonAsync(Project(x.S.ProjectId) + "/checkpoints", new SaveCheckpointRequest("Major", x.Major, new(Now))));
        foreach (var user in new[] { x.S.Scope.Users.Admin, x.S.Scope.Users.OtherLecturer, x.S.Scope.Users.Student, x.S.Scope.Users.Staff })
        {
            using var outsider = app.CreateAuthenticatedClient(user);
            Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PostAsJsonAsync(Evidence(x.Evidence) + "/reviews", Review(target))).StatusCode);
        }
        await using (var db = database.CreateContext()) { (await db.SupervisorAssignments.FindAsync(mentorAssignment))!.EndedAt = Now.AddHours(1); await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Forbidden, (await mentor.GetAsync(Evidence(x.Evidence) + "/reviews")).StatusCode);
        target = await Body<EvidenceReviewTarget>(await primary.GetAsync(Evidence(x.Evidence) + "/review-target"));
        await using (var db = database.CreateContext()) { (await db.Projects.FindAsync(x.S.ProjectId))!.Status = "ARCHIVED"; await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Conflict, (await primary.PostAsJsonAsync(Evidence(x.Evidence) + "/reviews", Review(target))).StatusCode);
        Assert.False((await Body<EvidenceReviewTarget>(await primary.GetAsync(Evidence(x.Evidence) + "/review-target"))).CanReview);
    }

    [Fact]
    public async Task Concurrent_review_and_audit_failure_preserve_append_only_history()
    {
        using var app = new TrackingFactory(database); var x = await Prepare(); using var primary = app.CreateAuthenticatedClient(x.Primary);
        var target = await Body<EvidenceReviewTarget>(await primary.GetAsync(Evidence(x.Evidence) + "/review-target"));
        app.FailAudit = true;
        Assert.Equal(HttpStatusCode.InternalServerError, (await primary.PostAsJsonAsync(Evidence(x.Evidence) + "/reviews", Review(target))).StatusCode);
        Assert.Empty((await Body<PagedResult<EvidenceReviewDto>>(await primary.GetAsync(Evidence(x.Evidence) + "/reviews"))).Items);
        app.FailAudit = false;
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => primary.PostAsJsonAsync(Evidence(x.Evidence) + "/reviews", Review(target))));
        Assert.Single(results.Where(r => r.IsSuccessStatusCode)); Assert.Single(results.Where(r => r.StatusCode == HttpStatusCode.Conflict));
        await database.Migrate(); await database.Migrate();
        Assert.Single((await Body<PagedResult<EvidenceReviewDto>>(await primary.GetAsync(Evidence(x.Evidence) + "/reviews"))).Items);
    }

    [Fact]
    public async Task Lead_schedules_qualified_committee_concurrent_room_booking_and_explicit_cancellation()
    {
        using var app = new TrackingFactory(database); var a = await Prepare(); var b = await Prepare();
        using var staffA = app.CreateAuthenticatedClient(a.S.Scope.Users.Staff); using var staffB = app.CreateAuthenticatedClient(b.S.Scope.Users.Staff);
        using var admin = app.CreateAuthenticatedClient(a.S.Scope.Users.Admin);
        var inputA = new CreateCommitteeRequest("Synthetic committee", [new(a.Primary, a.Major)]);
        Assert.Equal(HttpStatusCode.Conflict, (await staffA.PostAsJsonAsync(Project(a.S.ProjectId) + "/defense-committees", inputA)).StatusCode);
        await using (var db = database.CreateContext())
        {
            foreach (var item in new[] { a, b })
            {
                var profile = await db.SupervisorProfiles.SingleAsync(p => p.UserId == item.Primary); profile.IsAvailable = true;
                profile.SupervisorExpertises.Add(new() { ExpertiseName = (await db.Majors.FindAsync(item.Major))!.Code });
            }
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync(Project(a.S.ProjectId) + "/defense-committees", inputA)).StatusCode);
        var ca = await Body<CommitteeDto>(await staffA.PostAsJsonAsync(Project(a.S.ProjectId) + "/defense-committees", inputA));
        var cb = await Body<CommitteeDto>(await staffB.PostAsJsonAsync(Project(b.S.ProjectId) + "/defense-committees", new CreateCommitteeRequest("Other", [new(b.Primary, b.Major)])));
        Assert.False(ca.GrantsEvaluatorAuthority);
        var room = "E2E-" + Guid.NewGuid().ToString("N");
        var scheduleA = new ScheduleDefenseRequest(ca.Id, new(Now.AddHours(1)), new(Now.AddHours(2)), room);
        var scheduleB = scheduleA with { CommitteeId = cb.Id, Room = " " + room.ToLowerInvariant() + " " };
        var results = await Task.WhenAll(staffA.PostAsJsonAsync(Project(a.S.ProjectId) + "/defense-sessions", scheduleA), staffB.PostAsJsonAsync(Project(b.S.ProjectId) + "/defense-sessions", scheduleB));
        Assert.Single(results.Where(r => r.IsSuccessStatusCode)); Assert.Single(results.Where(r => r.StatusCode == HttpStatusCode.Conflict));
        var session = await Body<DefenseSessionDto>(results.Single(r => r.IsSuccessStatusCode));
        var owner = session.ProjectId == a.S.ProjectId ? staffA : staffB;
        var other = session.ProjectId == a.S.ProjectId ? staffB : staffA;
        var cancel = new CancelDefenseRequest(session.ConcurrencyToken, "Schedule correction");
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsJsonAsync($"/api/v1/defense-sessions/{session.Id}/cancel", cancel)).StatusCode);
        app.FailAudit = true;
        Assert.Equal(HttpStatusCode.InternalServerError, (await owner.PostAsJsonAsync($"/api/v1/defense-sessions/{session.Id}/cancel", cancel)).StatusCode);
        app.FailAudit = false;
        session = await Body<DefenseSessionDto>(await owner.PostAsJsonAsync($"/api/v1/defense-sessions/{session.Id}/cancel", cancel));
        Assert.Equal("CANCELLED", session.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync($"/api/v1/defense-sessions/{session.Id}/cancel", cancel)).StatusCode);
        await using var verify = database.CreateContext();
        Assert.Empty(await verify.Set<EvaluationAssignment>().Where(e => e.ProjectId == a.S.ProjectId || e.ProjectId == b.S.ProjectId).ToArrayAsync());
        Assert.Equal("FINAL_SUBMISSION", (await verify.Projects.FindAsync(a.S.ProjectId))!.Status);
        var notifications = await verify.Notifications.Where(n => n.RelatedEntityType == "DEFENSE_SESSION" && n.RelatedEntityId == session.Id).ToArrayAsync();
        Assert.Equal(2, notifications.Length);
        var notificationIds = notifications.Select(n => n.Id).ToArray();
        Assert.Equal(4, await verify.Set<NotificationEmailDelivery>().CountAsync(d => notificationIds.Contains(d.NotificationRecipient.NotificationId) && d.Status == "PENDING"));
    }
    [Fact]
    public async Task Deliverable_review_pins_explicit_version_and_file_manifest_without_exposing_storage()
    {
        using var app = new TrackingFactory(database); var x = await Prepare(); var foreign = await Prepare();
        using var primary = app.CreateAuthenticatedClient(x.Primary); long evidenceId; long firstId; long secondId; long fileId;
        await using (var db = database.CreateContext())
        {
            var deliverable = new M.Deliverable { ProjectId = x.S.ProjectId, Title = "Targeted report", Status = "OPEN", CreatedBy = x.S.Scope.Users.Student,
                EvidenceScope = "MAJOR_SPECIFIC", TargetMajorId = x.Major };
            var first = new M.DeliverableVersion { Deliverable = deliverable, VersionNumber = 1, SubmittedBy = x.S.Scope.Users.Student, Status = "SUBMITTED", SubmittedAt = Now };
            var second = new M.DeliverableVersion { Deliverable = deliverable, VersionNumber = 2, SubmittedBy = x.S.Scope.Users.Student, Status = "SUBMITTED", SubmittedAt = Now };
            var file = new M.File { UploadedBy = x.S.Scope.Users.Student, DeliverableVersion = first, OriginalFileName = "report.pdf", MimeType = "application/pdf",
                StoragePath = "private/exact-v1", ChecksumSha256 = new string('a', 64), FileSizeBytes = 100 };
            db.AddRange(first, second, file); await db.SaveChangesAsync(); firstId = first.Id; secondId = second.Id; fileId = file.Id;
            var evidence = new M.ProjectEvidence { ProjectId = x.S.ProjectId, SourceType = "DELIVERABLE", SourceId = deliverable.Id, DeliverableId = deliverable.Id,
                MajorId = x.Major, SubmittedBy = x.S.Scope.Users.Student, SubmittedAt = Now, VerificationStatus = "PENDING" };
            db.Add(evidence); await db.SaveChangesAsync(); evidenceId = evidence.Id;
        }
        Assert.Equal(HttpStatusCode.NotFound, (await primary.GetAsync(Evidence(evidenceId) + "/review-target")).StatusCode);
        var target = await Body<EvidenceReviewTarget>(await primary.GetAsync(Evidence(evidenceId) + $"/review-target?sourceVersionId={firstId}"));
        var review = await Body<EvidenceReviewDto>(await primary.PostAsJsonAsync(Evidence(evidenceId) + "/reviews", Review(target)));
        Assert.Equal(firstId, review.SourceVersionId);
        await using (var db = database.CreateContext()) { (await db.Files.FindAsync(fileId))!.StoragePath = "private/replaced"; await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Conflict, (await primary.PostAsJsonAsync(Evidence(evidenceId) + "/reviews", Review(target) with { ExpectedLatestReviewId = review.Id })).StatusCode);
        var secondTarget = await Body<EvidenceReviewTarget>(await primary.GetAsync(Evidence(evidenceId) + $"/review-target?sourceVersionId={secondId}"));
        Assert.NotEqual(target.SourceVersion, secondTarget.SourceVersion);
        Assert.Equal(HttpStatusCode.Forbidden, (await primary.GetAsync(Evidence(foreign.Evidence) + "/reviews")).StatusCode);
        var history = await primary.GetAsync(Evidence(evidenceId) + "/reviews");
        Assert.DoesNotContain("private/", await history.Content.ReadAsStringAsync());
        await using var verify = database.CreateContext();
        Assert.Contains("private/exact-v1", (await verify.Set<ProjectEvidenceReview>().FindAsync(review.Id))!.SourceSnapshotJson);
    }

    [Fact]
    public async Task Checkpoint_decision_audit_rollback_and_foreign_evidence_are_atomic()
    {
        using var app = new TrackingFactory(database); var x = await Prepare(); var foreign = await Prepare();
        using var primary = app.CreateAuthenticatedClient(x.Primary); using var foreignPrimary = app.CreateAuthenticatedClient(foreign.Primary);
        var foreignTarget = await Body<EvidenceReviewTarget>(await foreignPrimary.GetAsync(Evidence(foreign.Evidence) + "/review-target"));
        var review = await Body<EvidenceReviewDto>(await foreignPrimary.PostAsJsonAsync(Evidence(foreign.Evidence) + "/reviews", Review(foreignTarget)));
        var checkpoint = await Body<CheckpointDto>(await primary.PostAsJsonAsync(Project(x.S.ProjectId) + "/checkpoints", new SaveCheckpointRequest("Check", x.Major, new(Now))));
        var path = $"/api/v1/checkpoints/{checkpoint.Id}/decisions";
        Assert.Equal(HttpStatusCode.Forbidden, (await primary.PostAsJsonAsync(path,
            new DecideCheckpointRequest(checkpoint.ConcurrencyToken, "MET", "Foreign evidence", [new(foreign.Evidence, review.Id)]))).StatusCode);
        app.FailAudit = true;
        var input = new DecideCheckpointRequest(checkpoint.ConcurrencyToken, "AT_RISK", "Synthetic risk", []);
        Assert.Equal(HttpStatusCode.InternalServerError, (await primary.PostAsJsonAsync(path, input)).StatusCode);
        app.FailAudit = false;
        var history = await Body<PagedResult<CheckpointDto>>(await primary.GetAsync(Project(x.S.ProjectId) + "/checkpoints?pageSize=1"));
        Assert.Empty(history.Items.Single().Decisions); Assert.Equal(checkpoint.ConcurrencyToken, history.Items.Single().ConcurrencyToken);
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => primary.PostAsJsonAsync(path, input)));
        Assert.Single(responses.Where(r => r.IsSuccessStatusCode)); Assert.Single(responses.Where(r => r.StatusCode == HttpStatusCode.Conflict));
        await using var db = database.CreateContext();
        (await db.Set<ProjectRegistrationSnapshot>().SingleAsync(s => s.ProjectId == x.S.ProjectId)).SnapshotJson = "{}"; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await primary.GetAsync(Project(x.S.ProjectId) + "/checkpoints")).StatusCode);
    }

    [Fact]
    public async Task Committee_member_can_read_only_schedule_and_qualification_is_rechecked()
    {
        using var app = new TrackingFactory(database); var a = await Prepare(); var b = await Prepare();
        using var staffA = app.CreateAuthenticatedClient(a.S.Scope.Users.Staff); using var staffB = app.CreateAuthenticatedClient(b.S.Scope.Users.Staff);
        await using (var db = database.CreateContext())
        {
            var profile = new M.SupervisorProfile { UserId = a.S.Scope.Users.NewLecturer, IsAvailable = true };
            db.Add(profile);
            profile.SupervisorExpertises.Add(new() { ExpertiseName = (await db.Majors.FindAsync(a.Major))!.Code });
            await db.SaveChangesAsync();
        }
        var committee = await Body<CommitteeDto>(await staffA.PostAsJsonAsync(Project(a.S.ProjectId) + "/defense-committees",
            new CreateCommitteeRequest("Independent lecturer committee", [new(a.S.Scope.Users.NewLecturer, a.Major)])));
        using var lecturer = app.CreateAuthenticatedClient(a.S.Scope.Users.NewLecturer);
        var input = new ScheduleDefenseRequest(committee.Id, new(Now.AddHours(3)), new(Now.AddHours(4)), "MEMBER-" + Guid.NewGuid().ToString("N"));
        var session = await Body<DefenseSessionDto>(await staffA.PostAsJsonAsync(Project(a.S.ProjectId) + "/defense-sessions", input));
        var read = await Body<DefenseSessionDto>(await lecturer.GetAsync($"/api/v1/defense-sessions/{session.Id}")); Assert.False(read.CanCancel);
        Assert.Equal(HttpStatusCode.Forbidden, (await lecturer.GetAsync(Project(a.S.ProjectId) + "/defense-sessions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await lecturer.GetAsync(Evidence(a.Evidence) + "/review-target")).StatusCode);
        // Revoked qualification is checked again when scheduling a newly prepared committee.
        await using (var db = database.CreateContext()) { (await db.SupervisorProfiles.SingleAsync(p => p.UserId == a.S.Scope.Users.NewLecturer)).IsAvailable = false; await db.SaveChangesAsync(); }
        await Body<DefenseSessionDto>(await staffA.PostAsJsonAsync($"/api/v1/defense-sessions/{session.Id}/cancel", new CancelDefenseRequest(session.ConcurrencyToken, "Replan")));
        Assert.Equal(HttpStatusCode.Conflict, (await staffA.PostAsJsonAsync(Project(a.S.ProjectId) + "/defense-sessions", input)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staffB.GetAsync($"/api/v1/defense-sessions/{session.Id}")).StatusCode);
    }

    [Fact]
    public async Task Lecturer_conflict_cross_project_and_interdisciplinary_coverage_fail_closed()
    {
        using var app = new TrackingFactory(database); var a = await Prepare(); var b = await Prepare(); long additionalMajor;
        await using (var db = database.CreateContext())
        {
            // Prepare two synthetic projects in one academic organization for a shared lecturer.
            var semesterA = (await db.AcademicSemesters.FindAsync(a.S.Scope.SemesterId))!;
            (await db.AcademicSemesters.FindAsync(b.S.Scope.SemesterId))!.OrganizationId = semesterA.OrganizationId;
            (await db.Majors.FindAsync(b.Major))!.DepartmentId = a.S.Scope.Users.DepartmentId;
            (await db.Users.FindAsync(b.S.Scope.Users.Staff))!.DepartmentId = a.S.Scope.Users.DepartmentId;
            var snapshotB = await db.Set<ProjectRegistrationSnapshot>().SingleAsync(s => s.ProjectId == b.S.ProjectId);
            var registrationB = JsonSerializer.Deserialize<AIPMS.Application.Features.Projects.DTOs.RegistrationEvidence>(snapshotB.SnapshotJson)!;
            snapshotB.SnapshotJson = JsonSerializer.Serialize(registrationB with { OrganizationId = semesterA.OrganizationId,
                Scope = registrationB.Scope with { LeadDepartmentId = a.S.Scope.Users.DepartmentId }, DepartmentIds = [a.S.Scope.Users.DepartmentId],
                MajorDepartmentIds = new Dictionary<long, long> { [b.Major] = a.S.Scope.Users.DepartmentId } });
            var profile = await db.SupervisorProfiles.SingleAsync(p => p.UserId == a.Primary); profile.IsAvailable = true;
            foreach (var majorId in new[] { a.Major, b.Major }) profile.SupervisorExpertises.Add(new() { ExpertiseName = (await db.Majors.FindAsync(majorId))!.Code });
            var extra = new M.Major { Code = Guid.NewGuid().ToString("N"), Name = "Design", DepartmentId = a.S.Scope.Users.DepartmentId, IsActive = true };
            db.Add(extra); await db.SaveChangesAsync(); additionalMajor = extra.Id;
        }
        using var staffA = app.CreateAuthenticatedClient(a.S.Scope.Users.Staff); using var staffB = app.CreateAuthenticatedClient(b.S.Scope.Users.Staff);
        var committeeA = await Body<CommitteeDto>(await staffA.PostAsJsonAsync(Project(a.S.ProjectId) + "/defense-committees", new CreateCommitteeRequest("A", [new(a.Primary, a.Major)])));
        var committeeB = await Body<CommitteeDto>(await staffB.PostAsJsonAsync(Project(b.S.ProjectId) + "/defense-committees", new CreateCommitteeRequest("B", [new(a.Primary, b.Major)])));
        var inputA = new ScheduleDefenseRequest(committeeA.Id, new(Now.AddHours(5)), new(Now.AddHours(6)), "A-" + Guid.NewGuid().ToString("N"));
        var inputB = inputA with { CommitteeId = committeeB.Id, Room = "B-" + Guid.NewGuid().ToString("N") };
        var responses = await Task.WhenAll(staffA.PostAsJsonAsync(Project(a.S.ProjectId) + "/defense-sessions", inputA), staffB.PostAsJsonAsync(Project(b.S.ProjectId) + "/defense-sessions", inputB));
        Assert.Single(responses.Where(r => r.IsSuccessStatusCode)); Assert.Single(responses.Where(r => r.StatusCode == HttpStatusCode.Conflict));
        var session = await Body<DefenseSessionDto>(responses.Single(r => r.IsSuccessStatusCode));
        var client = session.ProjectId == a.S.ProjectId ? staffA : staffB;
        var duplicate = session.ProjectId == a.S.ProjectId ? inputA : inputB;
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Project(session.ProjectId) + "/defense-sessions", duplicate with { StartAt = new(Now.AddHours(7)), EndAt = new(Now.AddHours(8)) })).StatusCode);
        await using (var db = database.CreateContext())
        {
            var snapshot = await db.Set<ProjectRegistrationSnapshot>().SingleAsync(s => s.ProjectId == a.S.ProjectId);
            var registration = JsonSerializer.Deserialize<AIPMS.Application.Features.Projects.DTOs.RegistrationEvidence>(snapshot.SnapshotJson)!;
            snapshot.SnapshotJson = JsonSerializer.Serialize(registration with {
                Scope = registration.Scope with { ProjectMode = "INTERDISCIPLINARY", PrimaryMajorId = null,
                    Requirements = [.. registration.Scope.Requirements, new(additionalMajor, 0, 5, "Design evidence")] },
                MajorDepartmentIds = new Dictionary<long, long> { [a.Major] = a.S.Scope.Users.DepartmentId, [additionalMajor] = a.S.Scope.Users.DepartmentId } });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await staffA.PostAsJsonAsync(Project(a.S.ProjectId) + "/defense-committees", new CreateCommitteeRequest("Missing Design", [new(a.Primary, a.Major)]))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await staffA.PostAsJsonAsync(Project(a.S.ProjectId) + "/defense-committees", new CreateCommitteeRequest("No Design expertise", [new(a.Primary, a.Major), new(a.Primary, additionalMajor)]))).StatusCode);
        await using (var db = database.CreateContext())
        { (await db.SupervisorProfiles.SingleAsync(p => p.UserId == a.Primary)).SupervisorExpertises.Add(new() { ExpertiseName = (await db.Majors.FindAsync(additionalMajor))!.Code }); await db.SaveChangesAsync(); }
        Assert.Equal(2, (await Body<CommitteeDto>(await staffA.PostAsJsonAsync(Project(a.S.ProjectId) + "/defense-committees", new CreateCommitteeRequest("Covered", [new(a.Primary, a.Major), new(a.Primary, additionalMajor)])))).Members.Count);
    }

    [Fact]
    public async Task Swagger_exposes_success_payloads_for_tracking_weekly_and_cold_routes()
    {
        using var app = new TrackingFactory(database); using var client = app.CreateClient();
        var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var route in new[] { "/api/v1/project-evidence/{id}/review-target", "/api/v1/projects/{projectId}/checkpoints",
            "/api/v1/defense-sessions/{id}", "/api/v1/projects/{projectId}/weekly-assessments", "/api/v1/projects/{projectId}/cold/progress" })
            Assert.True(json.RootElement.GetProperty("paths").GetProperty(route).GetProperty("get").GetProperty("responses")
                .GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema").TryGetProperty("$ref", out _), route);
        foreach (var schema in new[] { "EvidenceReviewTarget", "CheckpointDto", "DefenseSessionDto", "WeeklyAssessmentDto", "ColdProgressDto" })
            Assert.True(json.RootElement.GetProperty("components").GetProperty("schemas").TryGetProperty(schema, out _), schema);
    }

    private sealed class TrackingFactory(EvaluationDraftDatabaseFixture database) : EvaluationFactory(database)
    {
        public bool FailAudit { get; set; }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => {
                var existing = services.Single(d => d.ServiceType == typeof(IAuditTrail)); services.Remove(existing);
                services.AddScoped<IAuditTrail>(p => new TrackingAudit((IAuditTrail)ActivatorUtilities.CreateInstance(p, existing.ImplementationType!), this));
            });
        }
    }
    private sealed class TrackingAudit(IAuditTrail inner, TrackingFactory app) : IAuditTrail
    {
        public async Task RecordAsync(AuditEntry entry, CancellationToken ct = default)
        { await inner.RecordAsync(entry, ct); if (app.FailAudit) throw new InvalidOperationException("Synthetic academic audit failure"); }
    }
}
