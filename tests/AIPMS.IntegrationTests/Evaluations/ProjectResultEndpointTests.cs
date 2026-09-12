using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Results.DTOs;
using AIPMS.Application.Features.Notifications.Abstractions;
using AIPMS.Application.Features.Notifications.Events;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed partial class EvaluationDraftEndpointTests
{
    private static string ResultUrl(long project) => $"/api/v1/projects/{project}/result";
    private static async Task<ResultPolicyDto> ResultPolicy(HttpClient staff, long project) =>
        await Body<ResultPolicyDto>(await staff.GetAsync(ResultUrl(project) + "-policy"));
    private static async Task<ProjectResultPreviewDto> ResultPreview(HttpClient staff, long project) =>
        await Body<ProjectResultPreviewDto>(await staff.GetAsync(ResultUrl(project) + "/preview"));
    private static Task<HttpResponseMessage> PublishResult(HttpClient staff, ProjectResultPreviewDto preview) =>
        staff.PostAsJsonAsync(ResultUrl(preview.ProjectId), new PublishProjectResultRequest(preview.ConfirmationToken));

    [Theory]
    [InlineData(8.6, "PASSED")]
    [InlineData(8.61, "FAILED")]
    public async Task Result_publication_is_immutable_completes_project_and_notifies_nonleader_member(decimal threshold, string outcome)
    {
        using var app = new EvaluationFactory(database);
        var (s, a, draft) = await Prepared(app);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student);
        await using (var db = database.CreateContext())
        {
            db.TeamMembers.Add(new() { TeamId = (await db.Projects.FindAsync(s.ProjectId))!.TeamId,
                AcademicSemesterId = s.Scope.SemesterId, UserId = s.Scope.Users.Student, IsLeader = false });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NotFound, (await student.GetAsync(ResultUrl(s.ProjectId))).StatusCode);
        var policy = await ResultPolicy(staff, s.ProjectId);
        await Body<ResultPolicyDto>(await staff.PutAsJsonAsync(ResultUrl(s.ProjectId) + "-policy",
            new ConfigureResultPolicyRequest(threshold, [new(a.Id, 100)], policy.ConcurrencyToken)));
        var waiting = await ResultPreview(staff, s.ProjectId);
        Assert.False(waiting.CanPublish);
        Assert.Contains($"EVALUATION_NOT_FINALIZED:{a.Id}", waiting.Blockers);
        Assert.Equal(HttpStatusCode.Conflict, (await PublishResult(staff, waiting)).StatusCode);
        await Body<EvaluationDraftDto>(await Finalize(lecturer, draft));
        Assert.Equal(HttpStatusCode.Conflict, (await PublishResult(staff, waiting)).StatusCode);
        // Publication remains possible after scoring closes; finalized evidence is authoritative.
        await using (var db = database.CreateContext())
        {
            (await db.ProjectPeriods.FindAsync(s.PeriodId))!.EndAt = EvaluationDraftDatabaseFixture.Now;
            (await db.Evaluations.FindAsync(draft.Id))!.TotalScore = 0;
            await db.SaveChangesAsync();
        }
        var preview = await ResultPreview(staff, s.ProjectId);
        Assert.True(preview.CanPublish);
        Assert.Equal(8.6m, preview.TotalScore);
        Assert.Equal(outcome, preview.Outcome);
        var response = await PublishResult(staff, preview);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await Body<ProjectResultDto>(response);
        Assert.Equal(outcome, result.Outcome);
        Assert.Equal(8.6m, Assert.Single(result.Contributions).Score);
        Assert.Equal(HttpStatusCode.Conflict, (await PublishResult(staff, preview)).StatusCode);
        var studentResponse = await student.GetAsync(ResultUrl(s.ProjectId));
        Assert.True(studentResponse.Headers.CacheControl!.NoStore);
        Assert.DoesNotContain("Good design", await studentResponse.Content.ReadAsStringAsync());
        Assert.Equal(result.Id, (await Body<ProjectResultDto>(studentResponse)).Id);
        await using (var db = database.CreateContext())
        {
            Assert.Equal("COMPLETED", (await db.Projects.FindAsync(s.ProjectId))!.Status);
            var notification = await db.Notifications.Include(n => n.NotificationRecipients).SingleAsync(n =>
                n.NotificationType == "PROJECT_RESULT_PUBLISHED" && n.RelatedEntityId == s.ProjectId);
            Assert.Equal("PROJECT", notification.RelatedEntityType);
            Assert.Equal(s.Scope.Users.Student, Assert.Single(notification.NotificationRecipients).UserId);
            Assert.Single(await db.AuditLogs.Where(audit => audit.Action == "PROJECT_RESULT_PUBLISHED" && audit.EntityId == result.Id.ToString()).ToListAsync());
            (await db.Set<ProjectResultPolicy>().FindAsync(s.ProjectId))!.PassThreshold = 0;
            await db.SaveChangesAsync();
        }
        await database.Migrate();
        var after = await Body<ProjectResultDto>(await student.GetAsync(ResultUrl(s.ProjectId)));
        Assert.Equal(threshold, after.PassThreshold);
        Assert.Equal(result.PolicyConcurrencyToken, after.PolicyConcurrencyToken);
    }

    [Fact]
    public async Task Required_weights_wait_for_every_finalization_and_freeze_revoke_and_policy()
    {
        using var app = new EvaluationFactory(database);
        var (s, first, draft) = await Prepared(app);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        using var secondLecturer = app.CreateAuthenticatedClient(s.Scope.Users.NewLecturer);
        var second = await Assign(staff, s, s.Scope.Users.NewLecturer);
        var secondDraft = await Create(secondLecturer, second.Id);
        secondDraft = await Body<EvaluationDraftDto>(await secondLecturer.PutAsJsonAsync(DraftUrl(secondDraft.Id) + "/draft",
            new SaveEvaluationDraftRequest(secondDraft.ConcurrencyToken, null, [new(s.Criteria[0], 5, null), new(s.Criteria[1], 10, null)])));
        var policy = await ResultPolicy(staff, s.ProjectId);
        var request = new ConfigureResultPolicyRequest(6, [new(first.Id, 40), new(second.Id, 60)], policy.ConcurrencyToken);
        var saved = await Body<ResultPolicyDto>(await staff.PutAsJsonAsync(ResultUrl(s.ProjectId) + "-policy", request));
        Assert.False(saved.IsLocked);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PutAsJsonAsync(ResultUrl(s.ProjectId) + "-policy", request)).StatusCode);
        await Body<EvaluationDraftDto>(await Finalize(lecturer, draft));
        Assert.True((await ResultPolicy(staff, s.ProjectId)).IsLocked);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PutAsJsonAsync(ResultUrl(s.ProjectId) + "-policy", request with { ConcurrencyToken = saved.ConcurrencyToken })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync($"/api/v1/evaluation-assignments/{second.Id}/revoke",
            new RevokeEvaluatorRequest(second.ConcurrencyToken, "Cannot remove required score"))).StatusCode);
        Assert.False((await ResultPreview(staff, s.ProjectId)).CanPublish);
        await Body<EvaluationDraftDto>(await Finalize(secondLecturer, secondDraft));
        var preview = await ResultPreview(staff, s.ProjectId);
        Assert.Equal(6.44m, preview.TotalScore);
        Assert.Equal(2, (await Body<ProjectResultDto>(await PublishResult(staff, preview))).Contributions.Count);
    }

    [Fact]
    public async Task Missing_policy_blocks_finalization_and_publication()
    {
        using var app = new EvaluationFactory(database);
        var s = await database.Seed();
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        var draft = await Create(lecturer, (await Assign(staff, s)).Id);
        draft = await Body<EvaluationDraftDto>(await Save(lecturer, draft, s));
        Assert.Equal(HttpStatusCode.Conflict, (await Finalize(lecturer, draft)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await staff.GetAsync(ResultUrl(s.ProjectId) + "-policy")).StatusCode);
        var preview = await ResultPreview(staff, s.ProjectId);
        Assert.Contains("RESULT_POLICY_REQUIRED", preview.Blockers);
        Assert.Equal(HttpStatusCode.Conflict, (await PublishResult(staff, preview)).StatusCode);
        await NotFinalized(draft.Id);
    }

    [Theory]
    [InlineData("student")]
    [InlineData("lecturer")]
    [InlineData("outside")]
    public async Task Result_management_requires_persisted_staff_scope(string role)
    {
        using var app = new EvaluationFactory(database);
        var (s, a, draft) = await Prepared(app);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        using var other = app.CreateAuthenticatedClient(role switch { "student" => s.Scope.Users.Student,
            "lecturer" => s.Scope.Users.Lecturer, _ => s.Scope.Users.OutsideStaff }, roles: ["ADMIN"]);
        var policy = await ResultPolicy(staff, s.ProjectId);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync(ResultUrl(s.ProjectId) + "-policy")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync(ResultUrl(s.ProjectId) + "/preview")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PutAsJsonAsync(ResultUrl(s.ProjectId) + "-policy",
            new ConfigureResultPolicyRequest(5, [new(a.Id, 100)], policy.ConcurrencyToken))).StatusCode);
        await Body<EvaluationDraftDto>(await Finalize(lecturer, draft));
        var preview = await ResultPreview(staff, s.ProjectId);
        Assert.Equal(HttpStatusCode.Forbidden, (await PublishResult(other, preview)).StatusCode);
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("notification")]
    public async Task Failed_result_publication_rolls_back_snapshot_status_and_side_effects(string failure)
    {
        using var setup = new EvaluationFactory(database);
        var (s, _, draft) = await Prepared(setup);
        using var lecturer = setup.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        using var staff = setup.CreateAuthenticatedClient(s.Scope.Users.Staff);
        await Body<EvaluationDraftDto>(await Finalize(lecturer, draft));
        var preview = await ResultPreview(staff, s.ProjectId);
        using var app = new FinalizationFailureFactory(database, failure);
        using var failing = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        Assert.Equal(HttpStatusCode.InternalServerError, (await PublishResult(failing, preview)).StatusCode);
        await using var db = database.CreateContext();
        Assert.Equal("FINAL_SUBMISSION", (await db.Projects.FindAsync(s.ProjectId))!.Status);
        Assert.False(await db.Set<ProjectResult>().AnyAsync(r => r.ProjectId == s.ProjectId));
        Assert.False(await db.Notifications.AnyAsync(n => n.NotificationType == "PROJECT_RESULT_PUBLISHED" && n.RelatedEntityId == s.ProjectId));
        Assert.Single(await db.Set<EvaluationFinalization>().Where(f => f.EvaluationId == draft.Id).ToListAsync());
        await Body<ProjectResultDto>(await PublishResult(staff, preview));
    }

    [Fact]
    public async Task Concurrent_publication_creates_one_result_and_unlisted_drafts_do_not_block_it()
    {
        using var app = new EvaluationFactory(database);
        var (s, _, draft) = await Prepared(app);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        using var second = app.CreateAuthenticatedClient(s.Scope.Users.NewLecturer);
        await Create(second, (await Assign(staff, s, s.Scope.Users.NewLecturer)).Id);
        await Body<EvaluationDraftDto>(await Finalize(lecturer, draft));
        var preview = await ResultPreview(staff, s.ProjectId);
        var responses = await Task.WhenAll(PublishResult(staff, preview), PublishResult(staff, preview));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        await using var db = database.CreateContext();
        Assert.Single(await db.Set<ProjectResult>().Where(r => r.ProjectId == s.ProjectId).ToListAsync());
    }

    [Fact]
    public async Task Concurrent_policy_change_and_first_finalization_cannot_change_a_frozen_policy()
    {
        using var app = new EvaluationFactory(database);
        var (s, a, draft) = await Prepared(app);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        var policy = await ResultPolicy(staff, s.ProjectId);
        var replies = await Task.WhenAll(Finalize(lecturer, draft), staff.PutAsJsonAsync(ResultUrl(s.ProjectId) + "-policy",
            new ConfigureResultPolicyRequest(9, [new(a.Id, 100)], policy.ConcurrencyToken)));
        Assert.All(replies, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        if (!replies[0].IsSuccessStatusCode) await Body<EvaluationDraftDto>(await Finalize(lecturer, draft));
        var locked = await ResultPolicy(staff, s.ProjectId);
        Assert.True(locked.IsLocked);
        Assert.Equal(replies[1].IsSuccessStatusCode ? 9 : 5, locked.PassThreshold);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PutAsJsonAsync(ResultUrl(s.ProjectId) + "-policy",
            new ConfigureResultPolicyRequest(0, [new(a.Id, 100)], locked.ConcurrencyToken))).StatusCode);
    }

    [Fact]
    public async Task Revoked_required_assignment_can_be_replaced_only_before_first_finalization()
    {
        using var app = new EvaluationFactory(database);
        var (s, first, draft) = await Prepared(app);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        var second = await Assign(staff, s, s.Scope.Users.NewLecturer);
        var policy = await ResultPolicy(staff, s.ProjectId);
        var saved = await Body<ResultPolicyDto>(await staff.PutAsJsonAsync(ResultUrl(s.ProjectId) + "-policy",
            new ConfigureResultPolicyRequest(5, [new(first.Id, 50), new(second.Id, 50)], policy.ConcurrencyToken)));
        await Body<EvaluationAssignmentDto>(await staff.PostAsJsonAsync($"/api/v1/evaluation-assignments/{second.Id}/revoke",
            new RevokeEvaluatorRequest(second.ConcurrencyToken, "Replace before scoring is locked")));
        Assert.Equal(HttpStatusCode.Conflict, (await Finalize(lecturer, draft)).StatusCode);
        await Body<ResultPolicyDto>(await staff.PutAsJsonAsync(ResultUrl(s.ProjectId) + "-policy",
            new ConfigureResultPolicyRequest(5, [new(first.Id, 100)], saved.ConcurrencyToken)));
        await Body<EvaluationDraftDto>(await Finalize(lecturer, draft));
    }

    [Fact]
    public async Task Cross_project_assignment_and_malformed_policy_are_rejected_without_changes()
    {
        using var app = new EvaluationFactory(database);
        var (s, a, _) = await Prepared(app);
        var (other, b, _) = await Prepared(app);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        var policy = await ResultPolicy(staff, s.ProjectId);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PutAsJsonAsync(ResultUrl(s.ProjectId) + "-policy",
            new ConfigureResultPolicyRequest(5, [new(b.Id, 100)], policy.ConcurrencyToken))).StatusCode);
        var invalid = await staff.PutAsJsonAsync(ResultUrl(s.ProjectId) + "-policy",
            new ConfigureResultPolicyRequest(5, [new(a.Id, 50), new(a.Id, 50)], policy.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("application/problem+json", invalid.Content.Headers.ContentType!.MediaType);
        Assert.Equal(policy.ConcurrencyToken, (await ResultPolicy(staff, s.ProjectId)).ConcurrencyToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(ResultUrl(other.ProjectId))).StatusCode);
        using var anonymous = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(ResultUrl(s.ProjectId))).StatusCode);
    }

    [Fact]
    public async Task Multidepartment_policy_requires_admin_and_cannot_be_replaced_by_one_department()
    {
        using var app = new EvaluationFactory(database);
        var (s, a, _) = await Prepared(app);
        using var admin = app.CreateAuthenticatedClient(s.Scope.Users.Admin);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        long outsideDepartment;
        await using (var db = database.CreateContext())
        {
            outsideDepartment = (await db.Users.FindAsync(s.Scope.Users.OtherLecturer))!.DepartmentId!.Value;
            db.ProjectMajors.Add(new() { ProjectId = s.ProjectId, Major = new() { Code = Guid.NewGuid().ToString("N"),
                Name = "Business", DepartmentId = outsideDepartment, IsActive = true } });
            // Seed a second protected rubric/assignment as supplied by that department's assignment workflow.
            var rubric = new AIPMS.Infrastructure.Persistence.Generated.Models.Rubric { Code = Guid.NewGuid().ToString("N"),
                Name = "Business rubric", DepartmentId = outsideDepartment, AcademicSemesterId = s.Scope.SemesterId,
                IsActive = true, CreatedBy = s.Scope.Users.OutsideStaff };
            db.Rubrics.Add(rubric);
            await db.SaveChangesAsync();
            db.Set<EvaluationAssignment>().Add(new() { ProjectId = s.ProjectId, EvaluatorId = s.Scope.Users.OtherLecturer,
                DepartmentId = outsideDepartment, ProjectPeriodId = s.PeriodId, RubricId = rubric.Id,
                EvaluationType = "LECTURER", Status = "ACTIVE", AssignedBy = s.Scope.Users.OutsideStaff,
                AssignedAt = EvaluationDraftDatabaseFixture.Now, ConcurrencyToken = Guid.NewGuid() });
            await db.SaveChangesAsync();
        }
        await using var verify = database.CreateContext();
        var b = await verify.Set<EvaluationAssignment>().SingleAsync(x => x.ProjectId == s.ProjectId && x.DepartmentId == outsideDepartment);
        var policy = await ResultPolicy(admin, s.ProjectId);
        var input = new ConfigureResultPolicyRequest(5, [new(a.Id, 50), new(b.Id, 50)], policy.ConcurrencyToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync(ResultUrl(s.ProjectId) + "-policy", input)).StatusCode);
        var saved = await Body<ResultPolicyDto>(await admin.PutAsJsonAsync(ResultUrl(s.ProjectId) + "-policy", input));
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(ResultUrl(s.ProjectId) + "/preview")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(ResultUrl(s.ProjectId) + "-policy")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync(ResultUrl(s.ProjectId) + "-policy",
            new ConfigureResultPolicyRequest(5, [new(a.Id, 100)], saved.ConcurrencyToken))).StatusCode);
        Assert.False((await ResultPreview(admin, s.ProjectId)).CanPublish);
    }

    [Fact]
    public async Task Result_notification_replay_preserves_single_delivery_and_excludes_left_or_inactive_members()
    {
        using var app = new EvaluationFactory(database);
        var (s, _, draft) = await Prepared(app);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        long leftId;
        long inactiveId;
        await using (var db = database.CreateContext())
        {
            var team = (await db.Projects.FindAsync(s.ProjectId))!.TeamId;
            var studentRole = await db.Roles.Where(r => r.Code == "STUDENT").Select(r => r.Id).SingleAsync();
            AIPMS.Infrastructure.Persistence.Generated.Models.User Member(bool left, bool inactive) => new()
            {
                Email = Guid.NewGuid().ToString("N") + "@example.test", FullName = "Member", PasswordHash = "unused",
                Status = inactive ? "INACTIVE" : "ACTIVE", DepartmentId = s.Scope.Users.DepartmentId,
                UserRoleUsers = [new() { RoleId = studentRole }],
                TeamMembers = [new() { TeamId = team, AcademicSemesterId = s.Scope.SemesterId,
                    JoinedAt = EvaluationDraftDatabaseFixture.Now.AddDays(-1),
                    LeftAt = left ? EvaluationDraftDatabaseFixture.Now : null }]
            };
            var left = Member(true, false);
            var inactive = Member(false, true);
            db.Users.AddRange(left, inactive);
            db.TeamMembers.Add(new() { UserId = s.Scope.Users.Student, TeamId = team,
                AcademicSemesterId = s.Scope.SemesterId, IsLeader = false });
            await db.SaveChangesAsync();
            leftId = left.Id; inactiveId = inactive.Id;
        }
        await Body<EvaluationDraftDto>(await Finalize(lecturer, draft));
        var result = await Body<ProjectResultDto>(await PublishResult(staff, await ResultPreview(staff, s.ProjectId)));
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AipmsDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<IWorkflowNotificationWriter>().WriteAsync(
                new(WorkflowNotificationKind.ProjectResultPublished, result.Id, s.Scope.Users.Staff, EvaluationDraftDatabaseFixture.Now), default);
            await transaction.CommitAsync();
        }
        await using var verify = database.CreateContext();
        var notification = Assert.Single(await verify.Notifications.Include(n => n.NotificationRecipients).Where(n =>
            n.RelatedEntityId == s.ProjectId && n.NotificationType == "PROJECT_RESULT_PUBLISHED").ToListAsync());
        Assert.Equal(s.Scope.Users.Student, Assert.Single(notification.NotificationRecipients).UserId);
        using var leftClient = app.CreateAuthenticatedClient(leftId);
        using var inactiveClient = app.CreateAuthenticatedClient(inactiveId);
        Assert.Equal(HttpStatusCode.Forbidden, (await leftClient.GetAsync(ResultUrl(s.ProjectId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await inactiveClient.GetAsync(ResultUrl(s.ProjectId))).StatusCode);
    }

    [Fact]
    public async Task Policy_audit_failure_preserves_previous_threshold_and_token()
    {
        using var app = new EvaluationFactory(database);
        var (s, a, _) = await Prepared(app);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        var policy = await ResultPolicy(staff, s.ProjectId);
        using var failing = new EvaluationFactory(database, failAudit: true);
        using var failedStaff = failing.CreateAuthenticatedClient(s.Scope.Users.Staff);
        Assert.Equal(HttpStatusCode.InternalServerError, (await failedStaff.PutAsJsonAsync(ResultUrl(s.ProjectId) + "-policy",
            new ConfigureResultPolicyRequest(9, [new(a.Id, 100)], policy.ConcurrencyToken))).StatusCode);
        var read = await ResultPolicy(staff, s.ProjectId);
        Assert.Equal(policy.PassThreshold, read.PassThreshold);
        Assert.Equal(policy.ConcurrencyToken, read.ConcurrencyToken);
    }
}
