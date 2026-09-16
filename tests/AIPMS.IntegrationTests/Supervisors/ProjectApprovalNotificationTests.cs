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
    private async Task<string> PrepareApproval(RequestProject project)
    {
        await using var db = database.CreateContext();
        var row = await db.Projects.SingleAsync(p => p.Id == project.Id);
        row.Status = "UNDER_REVIEW";
        await db.SaveChangesAsync();
        return Convert.ToBase64String(row.RowVersion);
    }

    [Fact]
    public async Task Approval_notifies_team_with_project_link_and_concurrent_replays_preserve_read_state()
    {
        var s = await database.SeedAsync();
        var p = await SeedProject(s);
        var token = await PrepareApproval(p);
        using var app = new SupervisorFactory(database, clock: new Clock());
        using var staff = app.CreateAuthenticatedClient(s.Staff, roles: AppRoles.DepartmentStaff);
        using var leader = app.CreateAuthenticatedClient(p.LeaderId);
        using var member = app.CreateAuthenticatedClient(p.MemberId);
        var project = await Body<ProjectDto>(await staff.PostAsJsonAsync($"/api/v1/projects/{p.Id}/approve", new { concurrencyToken = token }));
        var notification = Assert.Single(await Notifications(leader));
        Assert.Equal("PROJECT_APPROVED", notification.NotificationType);
        Assert.Equal("PROJECT", notification.RelatedEntityType);
        Assert.Equal(p.Id, notification.RelatedEntityId);
        Assert.Equal(Now, notification.CreatedAt);
        Assert.Equal(notification.Id, Assert.Single(await Notifications(member)).Id);
        Assert.Empty(await Notifications(staff));
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"/api/v1/projects/{notification.RelatedEntityId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await leader.PatchAsync($"/api/v1/notifications/{notification.Id}/read", null)).StatusCode);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AipmsDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<IWorkflowNotificationWriter>().WriteAsync(
                new(WorkflowNotificationKind.ProjectApproved, p.Id, s.Staff, Now), default);
            await transaction.CommitAsync();
        }));
        Assert.True(Assert.Single(await Notifications(leader)).IsRead);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync($"/api/v1/projects/{p.Id}/approve", new { concurrencyToken = project.ConcurrencyToken })).StatusCode);
        await using var verify = database.CreateContext();
        Assert.Single(await verify.ProjectStatusHistories.Where(h => h.ProjectId == p.Id && h.NewStatus == "APPROVED").ToListAsync());
        Assert.Equal(2, await verify.NotificationRecipients.CountAsync(r => r.NotificationId == notification.Id));
    }

    [Theory]
    [InlineData("LEFT")]
    [InlineData("INACTIVE")]
    [InlineData("ROLE_REMOVED")]
    [InlineData("OUTSIDE_ORGANIZATION")]
    public async Task Approval_excludes_ineligible_members(string reason)
    {
        var s = await database.SeedAsync();
        var p = await SeedProject(s);
        var token = await PrepareApproval(p);
        await using (var db = database.CreateContext())
        {
            if (reason == "LEFT")
            {
                var member = await db.TeamMembers.SingleAsync(m => m.TeamId == p.TeamId && m.UserId == p.MemberId);
                member.LeftAt = member.JoinedAt.AddSeconds(1);
            }
            if (reason == "INACTIVE") (await db.Users.FindAsync(p.MemberId))!.Status = "INACTIVE";
            if (reason == "ROLE_REMOVED") db.UserRoles.RemoveRange(await db.UserRoles.Where(r => r.UserId == p.MemberId).ToListAsync());
            if (reason == "OUTSIDE_ORGANIZATION") (await db.Users.FindAsync(p.MemberId))!.DepartmentId = null;
            await db.SaveChangesAsync();
        }
        using var app = new SupervisorFactory(database, clock: new Clock());
        using var staff = app.CreateAuthenticatedClient(s.Staff, roles: AppRoles.DepartmentStaff);
        await Body<ProjectDto>(await staff.PostAsJsonAsync($"/api/v1/projects/{p.Id}/approve", new { concurrencyToken = token }));
        await using var verify = database.CreateContext();
        var notification = await verify.Notifications.Include(n => n.NotificationRecipients).SingleAsync(n => n.RelatedEntityId == p.Id && n.NotificationType == "PROJECT_APPROVED");
        Assert.Equal(p.LeaderId, Assert.Single(notification.NotificationRecipients).UserId);
    }

    [Fact]
    public async Task Approval_notification_failure_rolls_back_status_history_and_audit()
    {
        var s = await database.SeedAsync();
        var p = await SeedProject(s);
        var token = await PrepareApproval(p);
        using var app = new SupervisorFactory(database, clock: new Clock(), saveInterceptor: new FailApprovalNotification());
        using var staff = app.CreateAuthenticatedClient(s.Staff, roles: AppRoles.DepartmentStaff);
        Assert.Equal(HttpStatusCode.InternalServerError, (await staff.PostAsJsonAsync($"/api/v1/projects/{p.Id}/approve", new { concurrencyToken = token })).StatusCode);
        await using var db = database.CreateContext();
        Assert.Equal("UNDER_REVIEW", (await db.Projects.FindAsync(p.Id))!.Status);
        Assert.False(await db.ProjectStatusHistories.AnyAsync(h => h.ProjectId == p.Id));
        Assert.False(await db.AuditLogs.AnyAsync(a => a.Action == "PROJECT_APPROVED" && a.EntityId == p.Id.ToString()));
        Assert.False(await db.Notifications.AnyAsync(n => n.NotificationType == "PROJECT_APPROVED" && n.RelatedEntityId == p.Id));
    }

    [Fact]
    public async Task Approval_stale_event_does_not_notify_and_old_link_does_not_grant_access()
    {
        var s = await database.SeedAsync();
        var p = await SeedProject(s);
        await PrepareApproval(p);
        using var app = new SupervisorFactory(database, clock: new Clock());
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AipmsDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<IWorkflowNotificationWriter>().WriteAsync(
                new(WorkflowNotificationKind.ProjectApproved, p.Id, s.Staff, Now), default);
            await transaction.CommitAsync();
        }
        await using var verify = database.CreateContext();
        Assert.False(await verify.Notifications.AnyAsync(n => n.NotificationType == "PROJECT_APPROVED" && n.RelatedEntityId == p.Id));
        using var outsider = app.CreateAuthenticatedClient(s.Student);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync($"/api/v1/projects/{p.Id}")).StatusCode);
    }

    private sealed class FailApprovalNotification : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<M.Notification>().Any(e => e.State == EntityState.Added && e.Entity.NotificationType == "PROJECT_APPROVED"))
                throw new InvalidOperationException("Injected approval notification failure");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
