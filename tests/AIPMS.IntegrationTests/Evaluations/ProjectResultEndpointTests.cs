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
    private static async Task<ProjectResultPreviewDto> ResultPreview(HttpClient staff, long project) =>
        await Body<ProjectResultPreviewDto>(await staff.GetAsync(ResultUrl(project) + "/preview"));
    private static Task<HttpResponseMessage> PublishResult(HttpClient staff, ProjectResultPreviewDto preview) =>
        staff.PostAsJsonAsync(ResultUrl(preview.ProjectId), new PublishProjectResultRequest(preview.ConfirmationToken));

    private async Task<(EvaluationScenario Scenario, EvaluationAssignmentDto Assignment, EvaluationDraftDto Draft)> PreparedResult(
        EvaluationFactory app, decimal threshold = 5, decimal? firstWeight = null)
    {
        var s = await database.Seed();
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        var input = await ScopedRequest(staff, s, single: true, threshold: threshold, firstWeight: firstWeight);
        var assignment = await Body<EvaluationAssignmentDto>(await staff.PostAsJsonAsync(AssignUrl(s.ProjectId), input));
        var draft = await Create(lecturer, assignment.Id);
        return (s, assignment, await Body<EvaluationDraftDto>(await Save(lecturer, draft, s)));
    }

    [Theory]
    [InlineData(8.6, "PASSED")]
    [InlineData(8.61, "FAILED")]
    public async Task Result_publication_is_immutable_completes_project_and_notifies_nonleader_member(decimal threshold, string outcome)
    {
        using var app = new EvaluationFactory(database);
        var (s, a, draft) = await PreparedResult(app, threshold);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student);
        Assert.Equal(HttpStatusCode.NotFound, (await student.GetAsync(ResultUrl(s.ProjectId))).StatusCode);
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
            (await db.Set<EvaluationScheme>().FindAsync(result.SchemeId))!.PassThreshold = 0;
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
        var (s, first, draft) = await PreparedResult(app, firstWeight: 40);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        using var secondLecturer = app.CreateAuthenticatedClient(s.Scope.Users.NewLecturer);
        var second = await Assign(staff, s, s.Scope.Users.NewLecturer);
        var secondDraft = await Create(secondLecturer, second.Id);
        secondDraft = await Body<EvaluationDraftDto>(await secondLecturer.PutAsJsonAsync(DraftUrl(secondDraft.Id) + "/draft",
            new SaveEvaluationDraftRequest(secondDraft.ConcurrencyToken, null, [new(s.Criteria[0], 5, null), new(s.Criteria[1], 10, null)])));
        await Body<EvaluationDraftDto>(await Finalize(lecturer, draft));
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync($"/api/v1/evaluation-assignments/{second.Id}/revoke",
            new RevokeEvaluatorRequest(second.ConcurrencyToken, "Cannot remove required score"))).StatusCode);
        Assert.False((await ResultPreview(staff, s.ProjectId)).CanPublish);
        await Body<EvaluationDraftDto>(await Finalize(secondLecturer, secondDraft));
        var preview = await ResultPreview(staff, s.ProjectId);
        Assert.Equal(6.44m, preview.TotalScore);
        Assert.Equal(2, (await Body<ProjectResultDto>(await PublishResult(staff, preview))).Contributions.Count);
    }

    [Theory]
    [InlineData("student")]
    [InlineData("lecturer")]
    [InlineData("outside")]
    public async Task Result_management_requires_persisted_staff_scope(string role)
    {
        using var app = new EvaluationFactory(database);
        var (s, a, draft) = await PreparedResult(app);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        using var other = app.CreateAuthenticatedClient(role switch { "student" => s.Scope.Users.Student,
            "lecturer" => s.Scope.Users.Lecturer, _ => s.Scope.Users.OutsideStaff }, roles: ["ADMIN"]);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync(ResultUrl(s.ProjectId) + "-policy")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync(ResultUrl(s.ProjectId) + "/preview")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PutAsJsonAsync(ResultUrl(s.ProjectId) + "-policy",
            new ConfigureResultPolicyRequest(5, [new(a.Id, 100)], null))).StatusCode);
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
        var (s, _, draft) = await PreparedResult(setup);
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
    public async Task Concurrent_publication_creates_one_result()
    {
        using var app = new EvaluationFactory(database);
        var (s, _, draft) = await PreparedResult(app);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        await Body<EvaluationDraftDto>(await Finalize(lecturer, draft));
        var preview = await ResultPreview(staff, s.ProjectId);
        var responses = await Task.WhenAll(PublishResult(staff, preview), PublishResult(staff, preview));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        await using var db = database.CreateContext();
        Assert.Single(await db.Set<ProjectResult>().Where(r => r.ProjectId == s.ProjectId).ToListAsync());
    }

    [Fact]
    public async Task Result_notification_replay_preserves_single_delivery_and_excludes_left_or_inactive_members()
    {
        using var app = new EvaluationFactory(database);
        var (s, _, draft) = await PreparedResult(app);
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

}
