using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Notifications.Abstractions;
using AIPMS.Application.Features.Notifications.DTOs;
using AIPMS.Application.Features.Notifications.Events;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.IntegrationTests.Teams;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Notifications;

public sealed class TeamNotificationEventTests(TeamDatabaseFixture database) : IClassFixture<TeamDatabaseFixture>
{
    private static async Task<T> Body<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private static async Task<TeamDto> Create(HttpClient client, TeamScenario s) =>
        await Body<TeamDto>(await client.PostAsJsonAsync("/api/v1/teams", new { academicSemesterId = s.SemesterId, code = "NTE", name = "Team" }));
    private static async Task<TeamInvitationDto> Invite(HttpClient client, long team, long user) =>
        await Body<TeamInvitationDto>(await client.PostAsJsonAsync($"/api/v1/teams/{team}/invitations", new { invitedUserId = user, message = "PRIVATE REQUEST TEXT" }));
    private static Task<HttpResponseMessage> Act(HttpClient client, long id, string action) =>
        client.PostAsync($"/api/v1/teams/invitations/{id}/{action}", null);
    private static async Task<NotificationDto[]> Inbox(HttpClient client) =>
        (await Body<PagedResult<NotificationDto>>(await client.GetAsync("/api/v1/notifications"))).Items.ToArray();

    [Fact]
    public async Task Events_reach_invitee_and_current_leader_without_exposing_messages_or_duplicating_replays()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s);
        using var leader = app.CreateAuthenticatedClient(s.Students[0]);
        using var nextLeader = app.CreateAuthenticatedClient(s.Students[1]);
        using var invitee = app.CreateAuthenticatedClient(s.Students[2]);
        var team = await Create(leader, s);
        var joining = await Invite(leader, team.Id, s.Students[1]);
        var sent = Assert.Single(await Inbox(nextLeader));
        Assert.Equal("TEAM_INVITATION_SENT", sent.NotificationType);
        Assert.Equal(joining.Id, sent.RelatedEntityId);
        Assert.Equal("TEAM_INVITATION", sent.RelatedEntityType);
        Assert.DoesNotContain("PRIVATE", sent.Content);
        await Body<TeamDto>(await Act(nextLeader, joining.Id, "accept"));
        Assert.Equal(HttpStatusCode.Conflict, (await Act(nextLeader, joining.Id, "accept")).StatusCode);
        Assert.Equal("TEAM_INVITATION_ACCEPTED", Assert.Single(await Inbox(leader)).NotificationType);
        var rejected = await Invite(leader, team.Id, s.Students[2]);
        await Body<TeamDto>(await leader.PostAsJsonAsync($"/api/v1/teams/{team.Id}/leader", new { newLeaderUserId = s.Students[1] }));
        Assert.Equal(HttpStatusCode.NoContent, (await Act(invitee, rejected.Id, "reject")).StatusCode);
        Assert.Contains(await Inbox(nextLeader), n => n.NotificationType == "TEAM_INVITATION_REJECTED" && n.RelatedEntityId == rejected.Id);
        Assert.DoesNotContain(await Inbox(leader), n => n.RelatedEntityId == rejected.Id);
        var cancelled = await Invite(nextLeader, team.Id, s.Students[2]);
        Assert.Equal(HttpStatusCode.NoContent, (await Act(nextLeader, cancelled.Id, "cancel")).StatusCode);
        Assert.Contains(await Inbox(invitee), n => n.NotificationType == "TEAM_INVITATION_CANCELLED" && n.RelatedEntityId == cancelled.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await Act(nextLeader, cancelled.Id, "cancel")).StatusCode);
        await using var db = database.CreateContext();
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.NotificationType == "TEAM_INVITATION_CANCELLED" && n.RelatedEntityId == cancelled.Id));
    }

    [Theory]
    [InlineData("INACTIVE")]
    [InlineData("ROLE_REMOVED")]
    public async Task Response_does_not_notify_inactive_or_no_longer_student_leader(string reason)
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s);
        using var leader = app.CreateAuthenticatedClient(s.Students[0]);
        using var invitee = app.CreateAuthenticatedClient(s.Students[1]);
        var team = await Create(leader, s);
        var invitation = await Invite(leader, team.Id, s.Students[1]);
        await using (var db = database.CreateContext())
        {
            if (reason == "INACTIVE") (await db.Users.FindAsync(s.Students[0]))!.Status = "INACTIVE";
            else db.UserRoles.RemoveRange(await db.UserRoles.Where(r => r.UserId == s.Students[0]).ToListAsync());
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NoContent, (await Act(invitee, invitation.Id, "reject")).StatusCode);
        await using var verify = database.CreateContext();
        Assert.False(await verify.Notifications.AnyAsync(n => n.NotificationType == "TEAM_INVITATION_REJECTED" && n.RelatedEntityId == invitation.Id));
        Assert.Equal("REJECTED", (await verify.TeamInvitations.FindAsync(invitation.Id))!.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Notification_or_later_audit_failure_rolls_back_send(bool failAudit)
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s, failAuditAction: failAudit ? "TEAM_INVITED" : null,
            customizeServices: services => { if (!failAudit) InjectNotificationFailure(services); });
        using var leader = app.CreateAuthenticatedClient(s.Students[0]);
        var team = await Create(leader, s);
        var response = await leader.PostAsJsonAsync($"/api/v1/teams/{team.Id}/invitations", new { invitedUserId = s.Students[1] });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await using var db = database.CreateContext();
        Assert.False(await db.TeamInvitations.AnyAsync(i => i.TeamId == team.Id));
        Assert.False(await db.NotificationRecipients.AnyAsync(r => r.UserId == s.Students[1]));
    }

    [Fact]
    public async Task Notification_failure_on_accept_rolls_back_membership_and_invitation()
    {
        var s = await database.SeedAsync();
        using var setup = new TeamTestFactory(database, s);
        using var leader = setup.CreateAuthenticatedClient(s.Students[0]);
        var team = await Create(leader, s);
        var invitation = await Invite(leader, team.Id, s.Students[1]);
        using var failing = new TeamTestFactory(database, s, customizeServices: InjectNotificationFailure);
        using var invitee = failing.CreateAuthenticatedClient(s.Students[1]);
        Assert.Equal(HttpStatusCode.InternalServerError, (await Act(invitee, invitation.Id, "accept")).StatusCode);
        await using var db = database.CreateContext();
        Assert.Equal("PENDING", (await db.TeamInvitations.FindAsync(invitation.Id))!.Status);
        Assert.False(await db.TeamMembers.AnyAsync(m => m.TeamId == team.Id && m.UserId == s.Students[1]));
        Assert.False(await db.Notifications.AnyAsync(n => n.RelatedEntityId == invitation.Id && n.NotificationType == "TEAM_INVITATION_ACCEPTED"));
    }

    [Fact]
    public async Task Duplicate_events_do_not_reset_read_state_or_create_new_recipients()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s);
        using var leader = app.CreateAuthenticatedClient(s.Students[0]);
        using var invitee = app.CreateAuthenticatedClient(s.Students[1]);
        var team = await Create(leader, s);
        var invitation = await Invite(leader, team.Id, s.Students[1]);
        var sent = Assert.Single(await Inbox(invitee));
        Assert.Equal(HttpStatusCode.NoContent, (await invitee.PatchAsync($"/api/v1/notifications/{sent.Id}/read", null)).StatusCode);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AipmsDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<IWorkflowNotificationWriter>().WriteAsync(
                new(WorkflowNotificationKind.TeamInvitationSent, invitation.Id, s.Students[0], TeamDatabaseFixture.Now), default);
            await transaction.CommitAsync();
        }));
        var after = Assert.Single(await Inbox(invitee));
        Assert.Equal(sent.Id, after.Id);
        Assert.True(after.IsRead);
        using var noTransaction = app.Services.CreateScope();
        await Assert.ThrowsAsync<InvalidOperationException>(() => noTransaction.ServiceProvider.GetRequiredService<IWorkflowNotificationWriter>()
            .WriteAsync(new(WorkflowNotificationKind.TeamInvitationSent, invitation.Id, s.Students[0], TeamDatabaseFixture.Now), default));
    }

    private void InjectNotificationFailure(IServiceCollection services)
    {
        services.RemoveAll<DbContextOptions<AipmsDbContext>>();
        services.AddDbContext<AipmsDbContext>(o => o.UseSqlServer(database.ConnectionString).AddInterceptors(new FailNotificationSave()));
    }

    private sealed class FailNotificationSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<M.Notification>().Any(e => e.State == EntityState.Added))
                throw new InvalidOperationException("Injected notification write failure");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
