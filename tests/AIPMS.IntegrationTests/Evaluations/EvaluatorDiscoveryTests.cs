using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Notifications.Abstractions;
using AIPMS.Application.Features.Notifications.Events;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed partial class EvaluationDraftEndpointTests
{
    private static string CandidatesUrl(EvaluationScenario s) => $"/api/v1/projects/{s.ProjectId}/eligible-evaluators?periodId={s.PeriodId}";

    [Fact]
    public async Task Candidates_respect_scope_type_active_roles_and_existing_assignments_with_stable_paging()
    {
        var s = await database.Seed();
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var admin = app.CreateAuthenticatedClient(s.Scope.Users.Admin);
        var page = await Body<PagedResult<EligibleEvaluatorDto>>(await staff.GetAsync(CandidatesUrl(s)));
        Assert.Equal(2, page.TotalCount);
        var primary = Assert.Single(page.Items.Where(i => i.UserId == s.Scope.Users.Lecturer));
        Assert.Equal(new[] { "LECTURER", "SUPERVISOR" }, primary.EvaluationTypes);
        Assert.Equal(new[] { "LECTURER" }, Assert.Single(page.Items.Where(i => i.UserId == s.Scope.Users.NewLecturer)).EvaluationTypes);
        var adminPage = await Body<PagedResult<EligibleEvaluatorDto>>(await admin.GetAsync(CandidatesUrl(s)));
        Assert.Equal(page.Items.Select(i => i.UserId), adminPage.Items.Select(i => i.UserId));
        var first = await Body<PagedResult<EligibleEvaluatorDto>>(await staff.GetAsync(CandidatesUrl(s) + "&pageSize=1"));
        var second = await Body<PagedResult<EligibleEvaluatorDto>>(await staff.GetAsync(CandidatesUrl(s) + "&pageSize=1&page=2"));
        Assert.Equal(page.Items.Select(i => i.UserId), first.Items.Concat(second.Items).Select(i => i.UserId));
        await Assign(staff, s);
        page = await Body<PagedResult<EligibleEvaluatorDto>>(await staff.GetAsync(CandidatesUrl(s)));
        Assert.Equal(new[] { "SUPERVISOR" }, Assert.Single(page.Items.Where(i => i.UserId == primary.UserId)).EvaluationTypes);
        await Assign(staff, s, type: "SUPERVISOR");
        Assert.DoesNotContain((await Body<PagedResult<EligibleEvaluatorDto>>(await staff.GetAsync(CandidatesUrl(s)))).Items, i => i.UserId == primary.UserId);
        await using var db = database.CreateContext();
        await db.Users.Where(u => u.Id == s.Scope.Users.NewLecturer).ExecuteUpdateAsync(x => x.SetProperty(u => u.Status, "INACTIVE"));
        Assert.Empty((await Body<PagedResult<EligibleEvaluatorDto>>(await staff.GetAsync(CandidatesUrl(s)))).Items);
        var rejected = await staff.PostAsJsonAsync(AssignUrl(s.ProjectId), new AssignEvaluatorRequest(s.Scope.Users.NewLecturer, s.PeriodId, "LECTURER"));
        await AssertCode(rejected, "EVALUATOR_INELIGIBLE");
    }

    [Fact]
    public async Task Candidates_reject_anonymous_forged_roles_outside_scope_and_invalid_pagination()
    {
        var s = await database.Seed();
        using var app = new EvaluationFactory(database);
        using var anonymous = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(CandidatesUrl(s))).StatusCode);
        foreach (var id in new[] { s.Scope.Users.Student, s.Scope.Users.Lecturer, s.Scope.Users.OutsideStaff })
        {
            using var denied = app.CreateAuthenticatedClient(id, roles: ["ADMIN"]);
            Assert.Equal(HttpStatusCode.Forbidden, (await denied.GetAsync(CandidatesUrl(s))).StatusCode);
        }
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        foreach (var query in new[] { "&page=0", "&pageSize=101", "&pageSize=0", "&page=1000001" })
            Assert.Equal(HttpStatusCode.BadRequest, (await staff.GetAsync(CandidatesUrl(s) + query)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync($"/api/v1/projects/9223372036854775807/eligible-evaluators?periodId={s.PeriodId}")).StatusCode);
    }

    [Theory]
    [InlineData("package", "FINAL_PACKAGE_REQUIRED")]
    [InlineData("window", "EVALUATION_WINDOW_CLOSED")]
    [InlineData("rubric", "PUBLISHED_RUBRIC_REQUIRED")]
    public async Task Discovery_and_assignment_share_stable_blocker_codes(string blocker, string code)
    {
        var s = await database.Seed(lockedSubmission: blocker != "package");
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        await using (var db = database.CreateContext())
        {
            if (blocker == "window") await db.ProjectPeriods.Where(p => p.Id == s.PeriodId).ExecuteUpdateAsync(x => x.SetProperty(p => p.Status, "CLOSED"));
            if (blocker == "rubric") await db.Set<RubricVersion>().Where(v => v.RubricId == s.RubricId).ExecuteUpdateAsync(x => x.SetProperty(v => v.Status, "DRAFT"));
        }
        await AssertCode(await staff.GetAsync(CandidatesUrl(s)), code);
        await AssertCode(await staff.PostAsJsonAsync(AssignUrl(s.ProjectId), new AssignEvaluatorRequest(s.Scope.Users.Lecturer, s.PeriodId, "LECTURER")), code);
    }

    [Fact]
    public async Task Supervisor_requirement_and_stale_draft_return_explicit_codes()
    {
        var s = await database.Seed();
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        await AssertCode(await staff.PostAsJsonAsync(AssignUrl(s.ProjectId), new AssignEvaluatorRequest(s.Scope.Users.NewLecturer, s.PeriodId, "SUPERVISOR")), "SUPERVISOR_REQUIRED");
        var draft = await Create(lecturer, (await Assign(staff, s)).Id);
        await Body<EvaluationDraftDto>(await Save(lecturer, draft, s));
        await AssertCode(await Save(lecturer, draft, s), "STALE_CONCURRENCY_TOKEN");
    }

    [Fact]
    public async Task Assignment_notifies_exact_recipient_once_without_email_even_on_replay()
    {
        var s = await database.Seed();
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        var assignment = await Assign(staff, s);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(AssignUrl(s.ProjectId), new AssignEvaluatorRequest(s.Scope.Users.Lecturer, s.PeriodId, "LECTURER"))).StatusCode);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AIPMS.Infrastructure.Persistence.Generated.AipmsDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<IWorkflowNotificationWriter>().WriteAsync(new(WorkflowNotificationKind.EvaluatorAssigned,
                assignment.Id, s.Scope.Users.Staff, EvaluationDraftDatabaseFixture.Now), default);
            await tx.CommitAsync();
        }
        await using var verify = database.CreateContext();
        var notification = await verify.Notifications.Include(n => n.NotificationRecipients).SingleAsync(n => n.NotificationType == "EVALUATOR_ASSIGNED" && n.RelatedEntityId == assignment.Id);
        var recipient = Assert.Single(notification.NotificationRecipients);
        Assert.Equal(s.Scope.Users.Lecturer, recipient.UserId);
        Assert.False(await verify.Set<AIPMS.Infrastructure.Persistence.Models.NotificationEmailDelivery>().AnyAsync(d => d.NotificationRecipientId == recipient.Id));
        Assert.Single(await verify.AuditLogs.Where(a => a.Action == "EVALUATOR_ASSIGNED" && a.EntityId == assignment.Id.ToString()).ToListAsync());
    }

    [Fact]
    public async Task Notification_failure_rolls_back_assignment_inbox_and_audit()
    {
        var s = await database.Seed();
        using var app = new FailedAssignmentNotificationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var setup = new EvaluationFactory(database);
        using var setupStaff = setup.CreateAuthenticatedClient(s.Scope.Users.Staff);
        var input = await ScopedRequest(setupStaff, s);
        Assert.Equal(HttpStatusCode.InternalServerError, (await staff.PostAsJsonAsync(AssignUrl(s.ProjectId), input)).StatusCode);
        await using var db = database.CreateContext();
        Assert.False(await db.Set<EvaluationAssignment>().AnyAsync(a => a.ProjectId == s.ProjectId));
        Assert.False(await db.AuditLogs.AnyAsync(a => a.ActorUserId == s.Scope.Users.Staff && a.Action == "EVALUATOR_ASSIGNED"));
        Assert.False(await db.Notifications.AnyAsync(n => n.CreatedBy == s.Scope.Users.Staff && n.NotificationType == "EVALUATOR_ASSIGNED"));
    }

    private static async Task AssertCode(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        Assert.True(json.RootElement.TryGetProperty("traceId", out _));
    }

    private sealed class FailedAssignmentNotificationFactory(EvaluationDraftDatabaseFixture database) : EvaluationFactory(database)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                var registration = services.Single(d => d.ServiceType == typeof(IWorkflowNotificationWriter));
                services.Remove(registration);
                services.AddScoped<IWorkflowNotificationWriter>(p => new FailingAssignmentWriter(
                    (IWorkflowNotificationWriter)ActivatorUtilities.CreateInstance(p, registration.ImplementationType!)));
            });
        }
    }
    private sealed class FailingAssignmentWriter(IWorkflowNotificationWriter inner) : IWorkflowNotificationWriter
    {
        public async Task WriteAsync(WorkflowNotificationEvent notification, CancellationToken ct)
        {
            await inner.WriteAsync(notification, ct);
            if (notification.Kind == WorkflowNotificationKind.EvaluatorAssigned) throw new InvalidOperationException("Injected assignment notification failure");
        }
    }
}
