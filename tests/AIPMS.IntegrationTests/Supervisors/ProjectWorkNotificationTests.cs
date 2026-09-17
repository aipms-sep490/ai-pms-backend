using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Notifications.Abstractions;
using AIPMS.Application.Features.Notifications.Events;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Supervisors;

public sealed partial class SupervisorRequestEndpointTests
{
    private static Task<HttpResponseMessage> ReviewProject(HttpClient client, long id, string action, string token) =>
        client.PostAsJsonAsync($"/api/v1/projects/{id}/{action}", new { concurrencyToken = token, reason = "PRIVATE review reason" });

    private static async Task Replay(SupervisorFactory app, WorkflowNotificationEvent notification)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AipmsDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        await scope.ServiceProvider.GetRequiredService<IWorkflowNotificationWriter>().WriteAsync(notification, default);
        await tx.CommitAsync();
    }

    [Theory]
    [InlineData("reject", "PROJECT_REJECTED", WorkflowNotificationKind.ProjectRejected)]
    [InlineData("revision", "PROJECT_REVISION_REQUESTED", WorkflowNotificationKind.ProjectRevisionRequested)]
    public async Task Work_notification_project_decision_is_atomic_scoped_and_replay_safe(string action, string type, WorkflowNotificationKind kind)
    {
        var s = await database.SeedAsync();
        var p = await SeedProject(s);
        var token = await PrepareApproval(p);
        using var app = new SupervisorFactory(database, clock: new Clock());
        using var staff = app.CreateAuthenticatedClient(s.Staff, roles: AppRoles.DepartmentStaff);
        using var outside = app.CreateAuthenticatedClient(s.OutsideStaff, roles: AppRoles.DepartmentStaff);
        using var leader = app.CreateAuthenticatedClient(p.LeaderId);
        using var member = app.CreateAuthenticatedClient(p.MemberId);
        Assert.Equal(HttpStatusCode.Forbidden, (await ReviewProject(outside, p.Id, action, token)).StatusCode);
        var updated = await Body<ProjectDto>(await ReviewProject(staff, p.Id, action, token));
        var notice = Assert.Single(await Notifications(leader));
        Assert.Equal(type, notice.NotificationType);
        Assert.Equal("PROJECT", notice.RelatedEntityType);
        Assert.Equal(p.Id, notice.RelatedEntityId);
        Assert.Equal(notice.Id, Assert.Single(await Notifications(member)).Id);
        Assert.DoesNotContain("PRIVATE", notice.Content);
        Assert.Empty(await Notifications(staff));
        await leader.PatchAsync($"/api/v1/notifications/{notice.Id}/read", null);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Replay(app, new(kind, p.Id, s.Staff, Now, updated.ConcurrencyToken))));
        Assert.True(Assert.Single(await Notifications(leader)).IsRead);
        Assert.Equal(HttpStatusCode.Conflict, (await ReviewProject(staff, p.Id, action, token)).StatusCode);

        using var failing = new SupervisorFactory(database, clock: new Clock(), saveInterceptor: new FailWorkNotification());
        using var failingStaff = failing.CreateAuthenticatedClient(s.Staff, roles: AppRoles.DepartmentStaff);
        var other = await SeedProject(s);
        var otherToken = await PrepareApproval(other);
        Assert.Equal(HttpStatusCode.InternalServerError, (await ReviewProject(failingStaff, other.Id, action, otherToken)).StatusCode);
        await using var db = database.CreateContext();
        Assert.Equal("UNDER_REVIEW", (await db.Projects.FindAsync(other.Id))!.Status);
        Assert.False(await db.ProjectStatusHistories.AnyAsync(h => h.ProjectId == other.Id));
        Assert.False(await db.AuditLogs.AnyAsync(a => a.EntityId == other.Id.ToString() && a.Action == type));
    }

    [Fact]
    public async Task Work_notification_each_revision_round_notifies_even_with_same_clock_and_old_event_is_ignored()
    {
        var s = await database.SeedAsync();
        var p = await SeedProject(s);
        using var app = new SupervisorFactory(database, clock: new Clock());
        using var staff = app.CreateAuthenticatedClient(s.Staff, roles: AppRoles.DepartmentStaff);
        using var leader = app.CreateAuthenticatedClient(p.LeaderId);
        var first = await Body<ProjectDto>(await ReviewProject(staff, p.Id, "revision", await PrepareApproval(p)));
        var original = Assert.Single(await Notifications(leader));
        await leader.PatchAsync($"/api/v1/notifications/{original.Id}/read", null);
        var second = await Body<ProjectDto>(await ReviewProject(staff, p.Id, "revision", await PrepareApproval(p)));
        await Replay(app, new(WorkflowNotificationKind.ProjectRevisionRequested, p.Id, s.Staff, Now, first.ConcurrencyToken));
        await Replay(app, new(WorkflowNotificationKind.ProjectRevisionRequested, p.Id, s.Staff, Now, second.ConcurrencyToken));
        var notices = await Notifications(leader);
        Assert.Equal(2, notices.Length);
        Assert.True(notices.Single(n => n.Id == original.Id).IsRead);
        Assert.False(notices.Single(n => n.Id != original.Id).IsRead);
    }

    private async Task<(RequestProject Project, long SourceId)> FeedbackSource(SupervisorScenario s, string sourceType)
    {
        var p = await SeedProject(s);
        await using var db = database.CreateContext();
        (await db.Projects.FindAsync(p.Id))!.Status = "ACTIVE";
        db.SupervisorAssignments.Add(new M.SupervisorAssignment { ProjectId = p.Id, SupervisorProfileId = s.ProfileId, IsPrimary = true,
            SupervisorRequest = new M.SupervisorRequest { ProjectId = p.Id, SupervisorProfileId = s.ProfileId,
                RequestedBy = p.LeaderId, Status = "ACCEPTED", RequestedAt = Now, RespondedAt = Now } });
        if (sourceType == "PROGRESS_REPORT")
        {
            var report = new M.ProgressReport { ProjectId = p.Id, SubmittedBy = p.LeaderId, ReportType = "WEEKLY", Summary = "Report",
                PeriodStart = DateOnly.FromDateTime(Now), PeriodEnd = DateOnly.FromDateTime(Now.AddDays(6)), Status = "SUBMITTED", SubmittedAt = Now };
            db.ProgressReports.Add(report);
            await db.SaveChangesAsync();
            return (p, report.Id);
        }
        if (sourceType == "MEETING")
        {
            var meeting = new M.Meeting { ProjectId = p.Id, Title = "Meeting", CreatedBy = s.Lecturer, StartAt = Now, Status = "COMPLETED" };
            db.Meetings.Add(meeting);
            await db.SaveChangesAsync();
            return (p, meeting.Id);
        }
        var version = new M.DeliverableVersion { SubmittedBy = p.LeaderId, VersionNumber = 1, Status = "SUBMITTED",
            Deliverable = new M.Deliverable { ProjectId = p.Id, Title = "Report", CreatedBy = p.LeaderId, Status = "OPEN" } };
        db.DeliverableVersions.Add(version);
        await db.SaveChangesAsync();
        return (p, version.Id);
    }

    private static string SourceUrl(string type, long id) => type switch
    {
        "PROGRESS_REPORT" => $"/api/v1/progress-reports/{id}",
        "MEETING" => $"/api/v1/meetings/{id}",
        _ => $"/api/v1/deliverable-versions/{id}"
    };
    private static Task<HttpResponseMessage> AddWorkFeedback(HttpClient client, string type, long id) => type == "DELIVERABLE_VERSION"
        ? client.PostAsJsonAsync(SourceUrl(type, id) + "/review", new { decision = "ACCEPTED", feedback = "PRIVATE feedback" })
        : client.PostAsJsonAsync(SourceUrl(type, id) + "/feedback", new { feedbackText = "PRIVATE feedback" });

    [Theory]
    [InlineData("PROGRESS_REPORT")]
    [InlineData("MEETING")]
    [InlineData("DELIVERABLE_VERSION")]
    public async Task Work_notification_feedback_has_valid_link_and_does_not_grant_access_after_departure(string sourceType)
    {
        var s = await database.SeedAsync();
        var (p, id) = await FeedbackSource(s, sourceType);
        using var app = new SupervisorFactory(database, clock: new Clock());
        using var lecturer = app.CreateAuthenticatedClient(s.Lecturer, roles: AppRoles.Lecturer);
        using var outsider = app.CreateAuthenticatedClient(s.OtherLecturer, roles: AppRoles.Lecturer);
        using var member = app.CreateAuthenticatedClient(p.MemberId);
        using var leader = app.CreateAuthenticatedClient(p.LeaderId);
        Assert.Equal(HttpStatusCode.Forbidden, (await AddWorkFeedback(outsider, sourceType, id)).StatusCode);
        var response = await AddWorkFeedback(lecturer, sourceType, id);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var notice = Assert.Single(await Notifications(member));
        Assert.Equal("SUPERVISOR_FEEDBACK_ADDED", notice.NotificationType);
        Assert.Equal(sourceType, notice.RelatedEntityType);
        Assert.Equal(id, notice.RelatedEntityId);
        Assert.DoesNotContain("PRIVATE", notice.Content);
        Assert.Equal(notice.Id, Assert.Single(await Notifications(leader)).Id);
        Assert.Empty(await Notifications(outsider));
        Assert.Empty(await Notifications(lecturer));
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync(SourceUrl(sourceType, id))).StatusCode);
        await using (var db = database.CreateContext())
        {
            var feedback = await db.SupervisorFeedbacks.SingleAsync(f => f.ProjectId == p.Id);
            await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Replay(app, new(WorkflowNotificationKind.SupervisorFeedbackAdded, feedback.Id, s.Lecturer, Now))));
            var departedMember = await db.TeamMembers.SingleAsync(m => m.TeamId == p.TeamId && m.UserId == p.MemberId);
            departedMember.LeftAt = departedMember.JoinedAt.AddSeconds(1);
            await db.SaveChangesAsync();
        }
        Assert.Single(await Notifications(member));
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync(SourceUrl(sourceType, id))).StatusCode);
        if (sourceType != "DELIVERABLE_VERSION")
        {
            var second = await AddWorkFeedback(lecturer, sourceType, id);
            Assert.True(second.IsSuccessStatusCode, await second.Content.ReadAsStringAsync());
            Assert.Equal(2, (await Notifications(leader)).Length);
            Assert.Single(await Notifications(member));
        }
    }

    [Theory]
    [InlineData("PROGRESS_REPORT")]
    [InlineData("MEETING")]
    [InlineData("DELIVERABLE_VERSION")]
    public async Task Work_notification_failure_rolls_back_feedback_status_and_audit(string sourceType)
    {
        var s = await database.SeedAsync();
        var (p, id) = await FeedbackSource(s, sourceType);
        using var app = new SupervisorFactory(database, clock: new Clock(), saveInterceptor: new FailWorkNotification());
        using var lecturer = app.CreateAuthenticatedClient(s.Lecturer, roles: AppRoles.Lecturer);
        Assert.Equal(HttpStatusCode.InternalServerError, (await AddWorkFeedback(lecturer, sourceType, id)).StatusCode);
        await using var db = database.CreateContext();
        Assert.False(await db.SupervisorFeedbacks.AnyAsync(f => f.ProjectId == p.Id));
        Assert.False(await db.AuditLogs.AnyAsync(a => a.ActorUserId == s.Lecturer));
        Assert.False(await db.Notifications.AnyAsync(n => n.CreatedBy == s.Lecturer));
        if (sourceType == "PROGRESS_REPORT") Assert.Equal("SUBMITTED", (await db.ProgressReports.FindAsync(id))!.Status);
        if (sourceType == "DELIVERABLE_VERSION") Assert.Equal("SUBMITTED", (await db.DeliverableVersions.FindAsync(id))!.Status);
        using var healthy = new SupervisorFactory(database, clock: new Clock());
        using var retry = healthy.CreateAuthenticatedClient(s.Lecturer, roles: AppRoles.Lecturer);
        var response = await AddWorkFeedback(retry, sourceType, id);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("revision", "LEFT")]
    [InlineData("revision", "INACTIVE")]
    [InlineData("revision", "ROLE_REMOVED")]
    [InlineData("revision", "DEPARTMENT_INACTIVE")]
    [InlineData("feedback", "LEFT")]
    [InlineData("feedback", "INACTIVE")]
    [InlineData("feedback", "ROLE_REMOVED")]
    [InlineData("feedback", "DEPARTMENT_INACTIVE")]
    public async Task Work_notification_excludes_ineligible_recipients_at_event_time(string action, string condition)
    {
        var s = await database.SeedAsync();
        var (p, id) = action == "feedback" ? await FeedbackSource(s, "PROGRESS_REPORT") : (await SeedProject(s), 0L);
        var token = action == "revision" ? await PrepareApproval(p) : "";
        await using (var db = database.CreateContext())
        {
            if (condition == "LEFT")
            {
                var departedMember = await db.TeamMembers.SingleAsync(m => m.TeamId == p.TeamId && m.UserId == p.MemberId);
                departedMember.LeftAt = departedMember.JoinedAt.AddSeconds(1);
            }
            if (condition == "INACTIVE") (await db.Users.FindAsync(p.MemberId))!.Status = "INACTIVE";
            if (condition == "ROLE_REMOVED") db.UserRoles.RemoveRange(await db.UserRoles.Where(r => r.UserId == p.MemberId).ToListAsync());
            if (condition == "DEPARTMENT_INACTIVE")
            {
                var department = new M.Department { Code = Guid.NewGuid().ToString("N"), Name = "Inactive",
                    OrganizationId = (await db.Departments.FindAsync(s.DepartmentId))!.OrganizationId, IsActive = false };
                (await db.Users.FindAsync(p.MemberId))!.Department = department;
            }
            await db.SaveChangesAsync();
        }
        using var app = new SupervisorFactory(database, clock: new Clock());
        using var actor = app.CreateAuthenticatedClient(action == "revision" ? s.Staff : s.Lecturer,
            roles: action == "revision" ? AppRoles.DepartmentStaff : AppRoles.Lecturer);
        var response = action == "revision" ? await ReviewProject(actor, p.Id, action, token) : await AddWorkFeedback(actor, "PROGRESS_REPORT", id);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var member = app.CreateAuthenticatedClient(p.MemberId);
        using var leader = app.CreateAuthenticatedClient(p.LeaderId);
        // An inactive account cannot open its inbox, so verify exclusion directly in SQL too.
        await using var verify = database.CreateContext();
        Assert.False(await verify.NotificationRecipients.AnyAsync(r => r.UserId == p.MemberId));
        Assert.Single(await Notifications(leader));
    }

    private sealed class FailWorkNotification : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<M.Notification>().Any(e => e.State == EntityState.Added
                && e.Entity.NotificationType is "PROJECT_REJECTED" or "PROJECT_REVISION_REQUESTED" or "SUPERVISOR_FEEDBACK_ADDED"))
                throw new IOException("Injected notification failure");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
