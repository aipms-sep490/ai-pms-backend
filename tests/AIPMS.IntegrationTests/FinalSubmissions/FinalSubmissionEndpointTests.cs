using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.FinalSubmissions.DTOs;
using AIPMS.Application.Features.Notifications.Abstractions;
using AIPMS.Application.Features.Notifications.Events;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.FinalSubmissions;

public sealed class FinalSubmissionEndpointTests(FinalSubmissionDraftDatabaseFixture database) : IClassFixture<FinalSubmissionDraftDatabaseFixture>
{
    private static DateTime Now => FinalSubmissionDraftDatabaseFixture.Now;
    private static string Route(long id) => $"/api/v1/projects/{id}/final-submission";
    private static string DraftRoute(long id) => $"/api/v1/projects/{id}/final-submission-draft";
    private static async Task<T> Body<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private sealed record Ready(FinalDraftScenario Scenario, DeliverableDto Deliverable, DeliverableVersionDto Version,
        FinalSubmissionDraftDto Draft, FinalRequirementsDto Requirements)
    {
        public SubmitFinalSubmissionRequest Input => new(Draft.ConcurrencyToken, Requirements.ConcurrencyToken!);
    }
    private async Task<Ready> Prepare(Factory app)
    {
        var s = await database.Seed();
        using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff);
        var item = await Body<DeliverableDto>(await leader.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/deliverables",
            new SaveDeliverableRequest(null, "Final report", null, "REPORT", Now.AddDays(1))));
        var version = await Upload(leader, item.Id, 0, "Original final report");
        var draft = await Body<FinalSubmissionDraftDto>(await leader.PostAsJsonAsync(DraftRoute(s.ProjectId),
            new CreateFinalSubmissionDraftRequest(s.PeriodId, "Final notes", [version.Id])));
        var requirements = await Body<FinalRequirementsDto>(await staff.PutAsJsonAsync(Route(s.ProjectId) + "/requirements",
            new ConfigureFinalRequirementsRequest([item.Id], null)));
        return new(s, item, version, draft, requirements);
    }
    private static async Task<DeliverableVersionDto> Upload(HttpClient client, long id, int latest, string text)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", "report.txt");
        form.Add(new StringContent(latest.ToString()), "expectedLatestVersion");
        return await Body<DeliverableVersionDto>(await client.PostAsync($"/api/v1/deliverables/{id}/versions", form));
    }

    [Fact]
    public async Task Submit_locks_exact_snapshot_notifies_department_and_preserves_metadata_and_file_access()
    {
        using var app = new Factory(database);
        var ready = await Prepare(app);
        var s = ready.Scenario;
        using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        using var member = app.CreateAuthenticatedClient(s.MemberId);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff);
        await Upload(leader, ready.Deliverable.Id, 1, "Newer version excluded");
        var check = await Body<FinalSubmissionChecklistDto>(await leader.GetAsync(Route(s.ProjectId) + "/checklist"));
        Assert.True(check.CanSubmit);
        Assert.True(Assert.Single(check.Items).IsComplete);
        var submitted = await Body<FinalSubmissionDto>(await leader.PostAsJsonAsync(Route(s.ProjectId), ready.Input));
        Assert.True(submitted.IsLocked);
        Assert.Equal("LOCKED", submitted.Status);
        Assert.Equal(Now, submitted.SubmittedAt);
        Assert.Equal(ready.Version.Id, Assert.Single(submitted.Items).DeliverableVersionId);
        Assert.True(submitted.Items[0].WasRequired);
        Assert.Equal("SUBMITTED", submitted.Items[0].StatusAtSubmission);
        var file = Assert.Single(submitted.Items[0].Files);
        foreach (var client in new[] { leader, member, staff })
        {
            var read = await client.GetAsync(Route(s.ProjectId));
            var json = await read.Content.ReadAsStringAsync();
            Assert.DoesNotContain("storageKey", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("no-store", read.Headers.CacheControl!.ToString());
            Assert.Equal("Original final report", await (await client.GetAsync(Route(s.ProjectId) + $"/files/{file.Id}/download")).Content.ReadAsStringAsync());
        }
        Assert.Equal(HttpStatusCode.NotFound, (await leader.GetAsync(Route(s.ProjectId) + "/files/9223372036854775807/download")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync(Route(s.ProjectId), ready.Input)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PutAsJsonAsync(DraftRoute(s.ProjectId),
            new UpdateFinalSubmissionDraftRequest(s.PeriodId, "Changed", [], ready.Draft.ConcurrencyToken))).StatusCode);
        Assert.True((await Body<FinalSubmissionDraftDto>(await member.GetAsync(DraftRoute(s.ProjectId)))).IsLocked);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PutAsJsonAsync(Route(s.ProjectId) + "/requirements",
            new ConfigureFinalRequirementsRequest([ready.Deliverable.Id], ready.Requirements.ConcurrencyToken))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.DeleteAsync($"/api/v1/files/{file.Id}")).StatusCode);
        await using (var db = database.CreateContext())
        {
            Assert.Equal("FINAL_SUBMISSION", (await db.Projects.FindAsync(s.ProjectId))!.Status);
            var notification = await db.Notifications.Include(n => n.NotificationRecipients).SingleAsync(n => n.NotificationType == "FINAL_SUBMISSION_LOCKED" && n.RelatedEntityType == "PROJECT" && n.RelatedEntityId == s.ProjectId);
            Assert.Equal(s.Users.Staff, Assert.Single(notification.NotificationRecipients).UserId);
            Assert.Single(await db.AuditLogs.Where(a => a.Action == "FINAL_SUBMISSION_LOCKED" && a.ActorUserId == s.Users.Student).ToListAsync());
            (await db.Deliverables.FindAsync(ready.Deliverable.Id))!.Title = "Changed live title";
            (await db.DeliverableVersions.FindAsync(ready.Version.Id))!.Status = "REJECTED";
            (await db.Files.FindAsync(file.Id))!.OriginalFileName = "changed.txt";
            await db.SaveChangesAsync();
        }
        var snapshot = await Body<FinalSubmissionDto>(await staff.GetAsync(Route(s.ProjectId)));
        Assert.Equal("Final report", snapshot.Items[0].Title);
        Assert.Equal("SUBMITTED", snapshot.Items[0].StatusAtSubmission);
        Assert.Equal("report.txt", snapshot.Items[0].Files[0].FileName);
        await database.Migrate();
        Assert.Equal(submitted.Id, (await Body<FinalSubmissionDto>(await member.GetAsync(Route(s.ProjectId)))).Id);
    }

    [Theory]
    [InlineData("member")]
    [InlineData("staff")]
    [InlineData("admin")]
    [InlineData("lecturer")]
    [InlineData("outsider")]
    public async Task Only_current_student_leader_can_submit(string role)
    {
        using var app = new Factory(database);
        var ready = await Prepare(app);
        var s = ready.Scenario;
        var id = role switch { "member" => s.MemberId, "staff" => s.Users.Staff, "admin" => s.Users.Admin,
            "lecturer" => s.Users.Lecturer, _ => s.Users.OtherLecturer };
        using var client = app.CreateAuthenticatedClient(id, roles: ["ADMIN", "STUDENT"]);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Route(s.ProjectId), ready.Input)).StatusCode);
        if (role == "member")
        {
            var check = await Body<FinalSubmissionChecklistDto>(await client.GetAsync(Route(s.ProjectId) + "/checklist"));
            Assert.False(check.CanSubmit);
            Assert.Contains("LEADER_REQUIRED", check.Blockers);
        }
        await Unsubmitted(s);
    }

    [Theory]
    [InlineData("student")]
    [InlineData("lecturer")]
    [InlineData("outsideStaff")]
    public async Task Requirements_cannot_be_changed_outside_managed_department(string role)
    {
        using var app = new Factory(database);
        var ready = await Prepare(app);
        var s = ready.Scenario;
        using var client = app.CreateAuthenticatedClient(role == "student" ? s.Users.Student : role == "lecturer" ? s.Users.Lecturer : s.Users.OutsideStaff);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(Route(s.ProjectId) + "/requirements",
            new ConfigureFinalRequirementsRequest([ready.Deliverable.Id], ready.Requirements.ConcurrencyToken))).StatusCode);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("requirements")]
    public async Task Stale_confirmation_tokens_reject_submission(string changed)
    {
        using var app = new Factory(database);
        var ready = await Prepare(app);
        var s = ready.Scenario;
        using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff);
        if (changed == "draft")
            await Body<FinalSubmissionDraftDto>(await leader.PutAsJsonAsync(DraftRoute(s.ProjectId),
                new UpdateFinalSubmissionDraftRequest(s.PeriodId, "Revised", [ready.Version.Id], ready.Draft.ConcurrencyToken)));
        else await Body<FinalRequirementsDto>(await staff.PutAsJsonAsync(Route(s.ProjectId) + "/requirements",
            new ConfigureFinalRequirementsRequest([ready.Deliverable.Id], ready.Requirements.ConcurrencyToken)));
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync(Route(s.ProjectId), ready.Input)).StatusCode);
        await Unsubmitted(s);
    }

    [Fact]
    public async Task Missing_configuration_missing_artifact_and_cross_project_requirement_fail_closed()
    {
        using var app = new Factory(database);
        var s = await database.Seed();
        using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff);
        var empty = await Body<FinalSubmissionChecklistDto>(await leader.GetAsync(Route(s.ProjectId) + "/checklist"));
        Assert.Contains("REQUIREMENTS_NOT_CONFIGURED", empty.Blockers);
        Assert.Contains("DRAFT_REQUIRED", empty.Blockers);
        var other = await Prepare(app);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PutAsJsonAsync(Route(s.ProjectId) + "/requirements",
            new ConfigureFinalRequirementsRequest([other.Deliverable.Id], null))).StatusCode);
        using var otherLeader = app.CreateAuthenticatedClient(other.Scenario.Users.Student);
        await Body<FinalSubmissionDraftDto>(await otherLeader.PutAsJsonAsync(DraftRoute(other.Scenario.ProjectId),
            new UpdateFinalSubmissionDraftRequest(other.Scenario.PeriodId, null, [], other.Draft.ConcurrencyToken)));
        var missing = await Body<FinalSubmissionChecklistDto>(await otherLeader.GetAsync(Route(other.Scenario.ProjectId) + "/checklist"));
        Assert.Contains($"REQUIRED_DELIVERABLE_MISSING:{other.Deliverable.Id}", missing.Blockers);
        Assert.False(Assert.Single(missing.Items).IsComplete);
        Assert.Equal(HttpStatusCode.Conflict, (await otherLeader.PostAsJsonAsync(Route(other.Scenario.ProjectId), other.Input)).StatusCode);
    }

    [Theory]
    [InlineData("missingFile")]
    [InlineData("corruptFile")]
    [InlineData("rejectedVersion")]
    [InlineData("invalidStorageKey")]
    [InlineData("deadline")]
    [InlineData("future")]
    [InlineData("periodInactive")]
    [InlineData("overlap")]
    [InlineData("semesterClosed")]
    [InlineData("projectClosed")]
    [InlineData("inactiveAccount")]
    [InlineData("leaderChanged")]
    public async Task Revalidates_artifacts_scope_state_and_window_before_submission(string change)
    {
        using var app = new Factory(database);
        var ready = await Prepare(app);
        var s = ready.Scenario;
        using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        await using (var db = database.CreateContext())
        {
            switch (change)
            {
                case "missingFile": app.Storage.Objects.Clear(); break;
                case "corruptFile": foreach (var key in app.Storage.Objects.Keys) app.Storage.Objects[key] = new byte[app.Storage.Objects[key].Length]; break;
                case "rejectedVersion": (await db.DeliverableVersions.FindAsync(ready.Version.Id))!.Status = "REJECTED"; break;
                case "invalidStorageKey": (await db.Files.FindAsync(ready.Version.Files[0].Id))!.StoragePath = "invalid"; break;
                case "deadline": app.Clock.Now = Now.AddHours(1); break;
                case "future": app.Clock.Now = Now.AddHours(-2); break;
                case "periodInactive": (await db.ProjectPeriods.FindAsync(s.PeriodId))!.Status = "CLOSED"; break;
                case "overlap": db.ProjectPeriods.Add(new() { AcademicSemesterId = s.SemesterId, Code = "OVERLAP", Name = "Overlap", PeriodType = "FINAL_SUBMISSION", Status = "ACTIVE", StartAt = Now.AddMinutes(-1), EndAt = Now.AddHours(1) }); break;
                case "semesterClosed": (await db.AcademicSemesters.FindAsync(s.SemesterId))!.Status = "CLOSED"; break;
                case "projectClosed": (await db.Projects.FindAsync(s.ProjectId))!.Status = "COMPLETED"; break;
                case "inactiveAccount": (await db.Users.FindAsync(s.Users.Student))!.Status = "INACTIVE"; break;
                case "leaderChanged": (await db.TeamMembers.SingleAsync(m => m.TeamId == s.TeamId && m.UserId == s.Users.Student)).IsLeader = false; break;
            }
            await db.SaveChangesAsync();
        }
        Assert.Equal(change is "inactiveAccount" or "leaderChanged" ? HttpStatusCode.Forbidden : HttpStatusCode.Conflict,
            (await leader.PostAsJsonAsync(Route(s.ProjectId), ready.Input)).StatusCode);
        await using var verify = database.CreateContext();
        Assert.False(await verify.Set<FinalSubmission>().AnyAsync(f => f.ProjectId == s.ProjectId));
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("deadlineDuringAudit")]
    [InlineData("notification")]
    public async Task Failure_after_snapshot_creation_rolls_back_entire_transition(string failure)
    {
        using var app = new Factory(database);
        var ready = await Prepare(app);
        app.FailAudit = failure == "audit";
        app.AdvanceDuringAudit = failure == "deadlineDuringAudit";
        app.FailNotification = failure == "notification";
        using var leader = app.CreateAuthenticatedClient(ready.Scenario.Users.Student);
        Assert.Equal(failure == "deadlineDuringAudit" ? HttpStatusCode.Conflict : HttpStatusCode.InternalServerError,
            (await leader.PostAsJsonAsync(Route(ready.Scenario.ProjectId), ready.Input)).StatusCode);
        await Unsubmitted(ready.Scenario);
    }

    [Fact]
    public async Task Concurrent_submissions_create_one_snapshot_and_notification_with_conflicts_instead_of_500()
    {
        using var app = new Factory(database);
        var ready = await Prepare(app);
        using var leader = app.CreateAuthenticatedClient(ready.Scenario.Users.Student);
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => leader.PostAsJsonAsync(Route(ready.Scenario.ProjectId), ready.Input)));
        Assert.Single(responses.Where(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        await using var db = database.CreateContext();
        var snapshot = Assert.Single(await db.Set<FinalSubmission>().Where(s => s.ProjectId == ready.Scenario.ProjectId).ToListAsync());
        Assert.Single(await db.Notifications.Where(n => n.NotificationType == "FINAL_SUBMISSION_LOCKED" && n.RelatedEntityType == "PROJECT" && n.RelatedEntityId == snapshot.ProjectId).ToListAsync());
    }

    [Fact]
    public async Task Locked_package_enables_assignment_and_assigned_evaluator_download_only_while_authorized()
    {
        using var app = new Factory(database);
        var ready = await Prepare(app);
        var s = ready.Scenario;
        using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff);
        using var evaluator = app.CreateAuthenticatedClient(s.Users.NewLecturer);
        using var outsider = app.CreateAuthenticatedClient(s.Users.OutsideStaff);
        long periodId;
        await using (var db = database.CreateContext())
        {
            var rubric = new M.Rubric { DepartmentId = s.Users.DepartmentId, AcademicSemesterId = s.SemesterId,
                Code = Guid.NewGuid().ToString("N"), Name = "Final rubric", IsActive = true, CreatedBy = s.Users.Staff,
                RubricCriteria = [new() { Criterion = new() { Code = Guid.NewGuid().ToString("N"), Name = "Quality", IsActive = true },
                    WeightPercent = 100, MaxScore = 10, SortOrder = 0, IsRequired = true }] };
            var period = new M.ProjectPeriod { AcademicSemesterId = s.SemesterId, Code = "EVAL", Name = "Evaluation", PeriodType = "EVALUATION",
                Status = "ACTIVE", StartAt = Now.AddHours(-1), EndAt = Now.AddDays(2), Rubric = rubric };
            db.ProjectPeriods.Add(period);
            await db.SaveChangesAsync();
            db.Set<RubricVersion>().Add(new() { RubricId = rubric.Id, RootRubricId = rubric.Id, VersionNumber = 1, Status = "PUBLISHED", ConcurrencyToken = Guid.NewGuid() });
            await db.SaveChangesAsync();
            periodId = period.Id;
        }
        var assignUrl = $"/api/v1/projects/{s.ProjectId}/evaluation-assignments";
        var input = new AssignEvaluatorRequest(s.Users.NewLecturer, periodId, "LECTURER");
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(assignUrl, input)).StatusCode);
        await Body<FinalSubmissionDto>(await leader.PostAsJsonAsync(Route(s.ProjectId), ready.Input));
        Assert.Equal(HttpStatusCode.Forbidden, (await evaluator.GetAsync(Route(s.ProjectId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(Route(s.ProjectId))).StatusCode);
        var assignment = await Body<EvaluationAssignmentDto>(await staff.PostAsJsonAsync(assignUrl, input));
        await Body<FinalSubmissionDto>(await evaluator.GetAsync(Route(s.ProjectId)));
        var fileRoute = Route(s.ProjectId) + $"/files/{ready.Version.Files[0].Id}/download";
        Assert.Equal("Original final report", await (await evaluator.GetAsync(fileRoute)).Content.ReadAsStringAsync());
        await Body<EvaluationDraftDto>(await evaluator.PostAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evaluation", null));
        await using (var db = database.CreateContext())
        {
            var row = (await db.Set<EvaluationAssignment>().FindAsync(assignment.Id))!;
            row.Status = "REVOKED";
            row.RevokedAt = Now;
            row.RevocationReason = "Access removed";
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await evaluator.GetAsync(fileRoute)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await evaluator.GetAsync(Route(s.ProjectId))).StatusCode);
    }

    private async Task Unsubmitted(FinalDraftScenario s)
    {
        await using var db = database.CreateContext();
        Assert.Equal("ACTIVE", (await db.Projects.FindAsync(s.ProjectId))!.Status);
        Assert.False(await db.Set<FinalSubmission>().AnyAsync(f => f.ProjectId == s.ProjectId));
        Assert.False(await db.AuditLogs.AnyAsync(a => a.ActorUserId == s.Users.Student && a.Action == "FINAL_SUBMISSION_LOCKED"));
        Assert.False(await db.Notifications.AnyAsync(n => n.CreatedBy == s.Users.Student && n.NotificationType == "FINAL_SUBMISSION_LOCKED"));
    }

    [Fact]
    public async Task Official_read_tracks_current_membership_and_primary_supervision()
    {
        using var app = new Factory(database);
        var ready = await Prepare(app);
        var s = ready.Scenario;
        using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        using var member = app.CreateAuthenticatedClient(s.MemberId);
        using var supervisor = app.CreateAuthenticatedClient(s.Users.Lecturer);
        await using (var db = database.CreateContext())
        {
            var request = new M.SupervisorRequest { ProjectId = s.ProjectId, SupervisorProfileId = s.Users.ProfileId,
                RequestedBy = s.Users.Student, Status = "ACCEPTED", RequestedAt = Now.AddDays(-1), RespondedAt = Now.AddHours(-1) };
            db.SupervisorAssignments.Add(new() { ProjectId = s.ProjectId, SupervisorRequest = request,
                SupervisorProfileId = s.Users.ProfileId, IsPrimary = true, AssignedAt = Now.AddHours(-1) });
            await db.SaveChangesAsync();
        }
        await Body<FinalSubmissionDto>(await leader.PostAsJsonAsync(Route(s.ProjectId), ready.Input));
        await Body<FinalSubmissionDto>(await supervisor.GetAsync(Route(s.ProjectId)));
        await Body<FinalSubmissionDto>(await member.GetAsync(Route(s.ProjectId)));
        await using (var db = database.CreateContext())
        {
            (await db.SupervisorAssignments.SingleAsync(a => a.ProjectId == s.ProjectId)).EndedAt = Now;
            (await db.TeamMembers.SingleAsync(m => m.TeamId == s.TeamId && m.UserId == s.MemberId)).LeftAt = Now;
            await db.SaveChangesAsync();
        }
        foreach (var client in new[] { member, supervisor })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Route(s.ProjectId))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Route(s.ProjectId) + $"/files/{ready.Version.Files[0].Id}/download")).StatusCode);
        }
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("requirements")]
    public async Task Concurrent_edit_and_submit_never_commit_stale_confirmation(string edit)
    {
        using var app = new Factory(database);
        var ready = await Prepare(app);
        var s = ready.Scenario;
        using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff);
        var submission = leader.PostAsJsonAsync(Route(s.ProjectId), ready.Input);
        var update = edit == "draft" ? leader.PutAsJsonAsync(DraftRoute(s.ProjectId),
            new UpdateFinalSubmissionDraftRequest(s.PeriodId, "Changed concurrently", [], ready.Draft.ConcurrencyToken))
            : staff.PutAsJsonAsync(Route(s.ProjectId) + "/requirements", new ConfigureFinalRequirementsRequest([ready.Deliverable.Id], ready.Requirements.ConcurrencyToken));
        var responses = await Task.WhenAll(submission, update);
        Assert.Single(responses.Where(r => r.IsSuccessStatusCode));
        Assert.Single(responses.Where(r => r.StatusCode == HttpStatusCode.Conflict));
        await using var db = database.CreateContext();
        if (responses[0].IsSuccessStatusCode)
        {
            var locked = await db.Set<FinalSubmission>().SingleAsync(f => f.ProjectId == s.ProjectId);
            Assert.Equal(Guid.Parse(ready.Draft.ConcurrencyToken), locked.DraftConcurrencyToken);
            Assert.Equal(Guid.Parse(ready.Requirements.ConcurrencyToken!), locked.RequirementsConcurrencyToken);
        }
        else await Unsubmitted(s);
    }

    [Fact]
    public async Task Requirements_updates_require_token_and_invalid_input_is_ProblemDetails()
    {
        using var app = new Factory(database);
        var ready = await Prepare(app);
        var s = ready.Scenario;
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff);
        using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        using var anonymous = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Route(s.ProjectId))).StatusCode);
        foreach (var input in new ConfigureFinalRequirementsRequest[] { new([], null), new([ready.Deliverable.Id, ready.Deliverable.Id], null), new(null!, null) })
        {
            var response = await staff.PutAsJsonAsync(Route(s.ProjectId) + "/requirements", input);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        }
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PutAsJsonAsync(Route(s.ProjectId) + "/requirements",
            new ConfigureFinalRequirementsRequest([ready.Deliverable.Id], null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await leader.PostAsJsonAsync(Route(s.ProjectId), new SubmitFinalSubmissionRequest("", ""))).StatusCode);
    }

    [Fact]
    public async Task Snapshot_remains_locked_even_if_project_status_is_changed_externally()
    {
        using var app = new Factory(database);
        var ready = await Prepare(app);
        var s = ready.Scenario;
        using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff);
        await Body<FinalSubmissionDto>(await leader.PostAsJsonAsync(Route(s.ProjectId), ready.Input));
        await using (var db = database.CreateContext())
        {
            (await db.Projects.FindAsync(s.ProjectId))!.Status = "ACTIVE";
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PutAsJsonAsync(DraftRoute(s.ProjectId),
            new UpdateFinalSubmissionDraftRequest(s.PeriodId, "Reopen", [], ready.Draft.ConcurrencyToken))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PutAsJsonAsync(Route(s.ProjectId) + "/requirements",
            new ConfigureFinalRequirementsRequest([ready.Deliverable.Id], ready.Requirements.ConcurrencyToken))).StatusCode);
        var check = await Body<FinalSubmissionChecklistDto>(await leader.GetAsync(Route(s.ProjectId) + "/checklist"));
        Assert.False(check.CanSubmit);
        Assert.Contains("ALREADY_SUBMITTED", check.Blockers);
    }
    private sealed class TestClock : TimeProvider
    {
        public DateTime Now { get; set; } = FinalSubmissionEndpointTests.Now;
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
    private sealed class MemoryStorage : IFileStorage
    {
        public ConcurrentDictionary<string, byte[]> Objects { get; } = new();
        public async Task WriteAsync(string key, Stream content, CancellationToken ct)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
            if (!Objects.TryAdd(key, buffer.ToArray())) throw new IOException("Already exists");
        }
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => key.Length != 32 ? throw new ArgumentException("Invalid key") : Objects.TryGetValue(key, out var bytes)
            ? Task.FromResult<Stream>(new MemoryStream(bytes, false)) : throw new FileNotFoundException();
        public Task DeleteAsync(string key, CancellationToken ct) { Objects.TryRemove(key, out _); return Task.CompletedTask; }
    }
    private sealed class Factory(FinalSubmissionDraftDatabaseFixture database) : AipmsWebApplicationFactory
    {
        public TestClock Clock { get; } = new();
        public MemoryStorage Storage { get; } = new();
        public bool FailAudit { get; set; }
        public bool AdvanceDuringAudit { get; set; }
        public bool FailNotification { get; set; }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = database.ConnectionString }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
                services.RemoveAll<IFileStorage>();
                services.AddSingleton<IFileStorage>(Storage);
                var notification = services.Single(d => d.ServiceType == typeof(IWorkflowNotificationWriter));
                services.Remove(notification);
                services.AddScoped<IWorkflowNotificationWriter>(p => new TestNotification(
                    (IWorkflowNotificationWriter)ActivatorUtilities.CreateInstance(p, notification.ImplementationType!), this));
                services.RemoveAll<IAuditTrail>();
                services.AddScoped<IAuditTrail>(p => new TestAudit(p.GetRequiredService<AipmsDbContext>(), this));
            });
        }
    }
    private sealed class TestNotification(IWorkflowNotificationWriter inner, Factory app) : IWorkflowNotificationWriter
    {
        public async Task WriteAsync(WorkflowNotificationEvent notification, CancellationToken ct)
        {
            await inner.WriteAsync(notification, ct);
            if (app.FailNotification) throw new InvalidOperationException("Injected notification failure");
        }
    }
    private sealed class TestAudit(AipmsDbContext db, Factory app) : IAuditTrail
    {
        public async Task RecordAsync(AuditEntry entry, CancellationToken ct = default)
        {
            if (entry.Action == "FINAL_SUBMISSION_LOCKED" && app.FailAudit) throw new InvalidOperationException("Injected audit failure");
            db.AuditLogs.Add(new() { ActorUserId = entry.ActorUserId, Action = entry.Action, EntityType = entry.EntityType,
                EntityId = entry.EntityId?.ToString(), OccurredAt = Now, Outcome = "SUCCESS" });
            await db.SaveChangesAsync(ct);
            if (entry.Action == "FINAL_SUBMISSION_LOCKED" && app.AdvanceDuringAudit) app.Clock.Now = Now.AddHours(1);
        }
    }
}
