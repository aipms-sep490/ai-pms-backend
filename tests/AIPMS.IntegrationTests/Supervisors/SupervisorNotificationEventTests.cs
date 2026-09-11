using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Notifications.DTOs;
using AIPMS.Application.Features.Supervisors.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Supervisors;

public sealed partial class SupervisorRequestEndpointTests
{
    private static async Task<NotificationDto[]> Notifications(HttpClient client) =>
        (await Body<PagedResult<NotificationDto>>(await client.GetAsync("/api/v1/notifications"))).Items.ToArray();

    [Theory]
    [InlineData("accept", "SUPERVISOR_REQUEST_ACCEPTED")]
    [InlineData("reject", "SUPERVISOR_REQUEST_REJECTED")]
    public async Task Decision_notifies_current_leader_and_send_notifies_only_requested_supervisor(string action, string type)
    {
        var s = await database.SeedAsync();
        var p = await SeedProject(s);
        using var app = new SupervisorFactory(database, clock: new Clock());
        using var leader = app.CreateAuthenticatedClient(p.LeaderId);
        using var lecturer = app.CreateAuthenticatedClient(s.Lecturer);
        using var currentLeader = app.CreateAuthenticatedClient(p.MemberId);
        using var outsider = app.CreateAuthenticatedClient(s.OtherLecturer);
        var request = await Body<SupervisorRequestDto>(await Send(leader, p.Id, s.ProfileId, "PRIVATE REQUEST"));
        var sent = Assert.Single(await Notifications(lecturer));
        Assert.Equal("SUPERVISOR_REQUEST_SENT", sent.NotificationType);
        Assert.Equal("SUPERVISOR_REQUEST", sent.RelatedEntityType);
        Assert.Equal(request.Id, sent.RelatedEntityId);
        Assert.DoesNotContain("PRIVATE", sent.Content);
        Assert.Empty(await Notifications(outsider));
        await using (var db = database.CreateContext())
        {
            (await db.TeamMembers.SingleAsync(m => m.TeamId == p.TeamId && m.UserId == p.LeaderId)).IsLeader = false;
            await db.SaveChangesAsync();
            (await db.TeamMembers.SingleAsync(m => m.TeamId == p.TeamId && m.UserId == p.MemberId)).IsLeader = true;
            await db.SaveChangesAsync();
        }
        await Body<SupervisorRequestDto>(await Respond(lecturer, request.Id, action));
        await Body<SupervisorRequestDto>(await Respond(lecturer, request.Id, action));
        var response = Assert.Single(await Notifications(currentLeader));
        Assert.Equal(type, response.NotificationType);
        Assert.Equal(request.Id, response.RelatedEntityId);
        Assert.Empty(await Notifications(leader));
    }

    [Fact]
    public async Task Manual_and_automatic_cancellations_notify_affected_lecturers_once()
    {
        var s = await database.SeedAsync();
        var p = await SeedProject(s);
        var otherProfile = await AddProfile(s.NewLecturer);
        using var app = new SupervisorFactory(database, clock: new Clock());
        using var leader = app.CreateAuthenticatedClient(p.LeaderId);
        using var lecturer = app.CreateAuthenticatedClient(s.Lecturer);
        using var other = app.CreateAuthenticatedClient(s.NewLecturer);
        var manual = await Body<SupervisorRequestDto>(await Send(leader, p.Id, s.ProfileId));
        await Body<SupervisorRequestDto>(await leader.PostAsync(ActionUrl(manual.Id, "cancel"), null));
        await Body<SupervisorRequestDto>(await leader.PostAsync(ActionUrl(manual.Id, "cancel"), null));
        Assert.Single((await Notifications(lecturer)).Where(n => n.NotificationType == "SUPERVISOR_REQUEST_CANCELLED" && n.RelatedEntityId == manual.Id));
        var selected = await Body<SupervisorRequestDto>(await Send(leader, p.Id, s.ProfileId));
        var auto = await Body<SupervisorRequestDto>(await Send(leader, p.Id, otherProfile));
        await Body<SupervisorRequestDto>(await Respond(lecturer, selected.Id, "accept"));
        await Body<SupervisorRequestDto>(await Respond(lecturer, selected.Id, "accept"));
        Assert.Single((await Notifications(other)).Where(n => n.NotificationType == "SUPERVISOR_REQUEST_CANCELLED" && n.RelatedEntityId == auto.Id));
        Assert.Single(await Notifications(leader));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Notification_or_final_audit_failure_rolls_back_accept_and_every_new_notification(bool auditFailure)
    {
        var s = await database.SeedAsync();
        var p = await SeedProject(s);
        var otherProfile = await AddProfile(s.NewLecturer);
        using var setup = new SupervisorFactory(database, clock: new Clock());
        using var leader = setup.CreateAuthenticatedClient(p.LeaderId);
        var selected = await Body<SupervisorRequestDto>(await Send(leader, p.Id, s.ProfileId));
        var other = await Body<SupervisorRequestDto>(await Send(leader, p.Id, otherProfile));
        using var failing = new SupervisorFactory(database, clock: new Clock(),
            saveInterceptor: auditFailure ? new FailActivationAudit() : new FailAcceptedNotification());
        using var lecturer = failing.CreateAuthenticatedClient(s.Lecturer);
        Assert.Equal(HttpStatusCode.InternalServerError, (await Respond(lecturer, selected.Id, "accept")).StatusCode);
        await using var db = database.CreateContext();
        Assert.Equal("PENDING", (await db.SupervisorRequests.FindAsync(selected.Id))!.Status);
        Assert.Equal("PENDING", (await db.SupervisorRequests.FindAsync(other.Id))!.Status);
        Assert.Equal("APPROVED", (await db.Projects.FindAsync(p.Id))!.Status);
        Assert.False(await db.SupervisorAssignments.AnyAsync(a => a.ProjectId == p.Id));
        var notifications = await db.Notifications.Where(n => n.RelatedEntityType == "SUPERVISOR_REQUEST"
            && (n.RelatedEntityId == selected.Id || n.RelatedEntityId == other.Id)).ToListAsync();
        Assert.Equal(2, notifications.Count);
        Assert.All(notifications, n => Assert.Equal("SUPERVISOR_REQUEST_SENT", n.NotificationType));
    }

    private sealed class FailAcceptedNotification : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<M.Notification>()
                .Any(e => e.State == EntityState.Added && e.Entity.NotificationType == "SUPERVISOR_REQUEST_ACCEPTED"))
                throw new InvalidOperationException("Injected notification failure");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
