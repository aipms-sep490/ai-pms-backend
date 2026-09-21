using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Task = System.Threading.Tasks.Task;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Teams;

public sealed partial class TeamEndpointTests
{
    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("ended-approve")]
    [InlineData("ended-reject")]
    [InlineData("inactive-mentor")]
    [InlineData("wrong-mentor")]
    [InlineData("qualification-revoked")]
    [InlineData("target-left")]
    [InlineData("current-leader-changed")]
    [InlineData("concurrent-approve")]
    [InlineData("notification-failure")]
    [InlineData("audit-failure")]
    public async Task Assigned_mentor_must_approve_leader_change_before_membership_changes(string decision)
    {
        var scenario = await database.SeedAsync();
        if (decision == "qualification-revoked")
            await EnableQualificationAsync(scenario, scenario.Students[0], scenario.Students[1]);
        using var app = new TeamTestFactory(database, scenario);
        using var leader = app.CreateAuthenticatedClient(scenario.Students[0]);
        using var member = app.CreateAuthenticatedClient(scenario.Students[1]);
        var team = await CreateAsync(leader, scenario);
        var invitation = await InviteAsync(leader, team.Id, scenario.Students[1]);
        await BodyAsync<TeamDto>(await AcceptAsync(member, invitation.Id));

        long mentorId;
        long nonPrimaryMentorId;
        await using (var db = database.CreateContext())
        {
            var department = await db.Departments.SingleAsync(d => d.Id ==
                db.Users.Where(u => u.Id == scenario.Students[0]).Select(u => u.DepartmentId).First());
            var lecturerRole = await db.Roles.SingleOrDefaultAsync(r => r.Code == AppRoles.Lecturer);
            if (lecturerRole is null)
            {
                lecturerRole = new Role { Code = AppRoles.Lecturer, Name = "Lecturer", IsSystemRole = true };
                db.Roles.Add(lecturerRole);
            }
            var mentor = new User
            {
                Email = $"mentor-{Guid.NewGuid():N}@example.test", FullName = "Assigned Mentor",
                PasswordHash = "unused-test-hash", Status = "ACTIVE", DepartmentId = department.Id,
                EmployeeCode = $"M-{Guid.NewGuid():N}"[..10],
                UserRoleUsers = [new UserRole { Role = lecturerRole }]
            };
            var profile = new SupervisorProfile { User = mentor, IsAvailable = true,
                CreatedAt = TeamDatabaseFixture.Now, UpdatedAt = TeamDatabaseFixture.Now };
            var nonPrimaryMentor = new User
            {
                Email = $"non-primary-mentor-{Guid.NewGuid():N}@example.test", FullName = "Non-primary Mentor",
                PasswordHash = "unused-test-hash", Status = "ACTIVE", DepartmentId = department.Id,
                EmployeeCode = $"NP-{Guid.NewGuid():N}"[..10],
                UserRoleUsers = [new UserRole { Role = lecturerRole }]
            };
            var nonPrimaryProfile = new SupervisorProfile { User = nonPrimaryMentor, IsAvailable = true,
                CreatedAt = TeamDatabaseFixture.Now, UpdatedAt = TeamDatabaseFixture.Now };
            var project = new Project
            {
                TeamId = team.Id, Code = $"P-{Guid.NewGuid():N}"[..12], Title = "Assigned project",
                Status = "ACTIVE", CreatedBy = scenario.Students[0], RegisteredAt = TeamDatabaseFixture.Now,
                CreatedAt = TeamDatabaseFixture.Now, UpdatedAt = TeamDatabaseFixture.Now
            };
            var supervisorRequest = new SupervisorRequest
            {
                Project = project, SupervisorProfile = profile, RequestedBy = scenario.Students[0],
                Status = "ACCEPTED", RequestedAt = TeamDatabaseFixture.Now,
                RespondedAt = TeamDatabaseFixture.Now, CreatedAt = TeamDatabaseFixture.Now,
                UpdatedAt = TeamDatabaseFixture.Now
            };
            db.SupervisorAssignments.Add(new SupervisorAssignment
            {
                Project = project, SupervisorProfile = profile, SupervisorRequest = supervisorRequest,
                IsPrimary = true, AssignedAt = TeamDatabaseFixture.Now,
                CreatedAt = TeamDatabaseFixture.Now, UpdatedAt = TeamDatabaseFixture.Now
            });
            db.SupervisorProfiles.Add(nonPrimaryProfile);
            await db.SaveChangesAsync();
            mentorId = mentor.Id;
            nonPrimaryMentorId = nonPrimaryMentor.Id;
        }

        var pending = await BodyAsync<TeamLeaderChangeRequestDto>(await leader.PostAsJsonAsync(
            $"/api/v1/teams/{team.Id}/leader", new { newLeaderUserId = scenario.Students[1] }));
        Assert.Equal("PENDING", pending.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync(
            $"/api/v1/teams/{team.Id}/leader-change-requests",
            new { newLeaderUserId = scenario.Students[1] })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync(
            $"/api/v1/team-leader-change-requests/{pending.Id}/approve", new { })).StatusCode);
        var history = await BodyAsync<PagedResult<TeamLeaderChangeRequestDto>>(
            await member.GetAsync($"/api/v1/team-leader-change-requests?teamId={team.Id}"));
        Assert.Contains(history.Items, item => item.Id == pending.Id);
        await using (var noticeDb = database.CreateContext())
        {
            var notice = await noticeDb.Notifications.Include(n => n.NotificationRecipients)
                .SingleAsync(n => n.RelatedEntityType == "TEAM_LEADER_CHANGE_REQUEST"
                    && n.RelatedEntityId == pending.Id && n.NotificationType == "TEAM_LEADER_CHANGE_REQUESTED");
            Assert.Equal(mentorId, Assert.Single(notice.NotificationRecipients).UserId);
        }
        await using (var before = database.CreateContext())
            Assert.Equal(scenario.Students[0], await before.TeamMembers
                .Where(m => m.TeamId == team.Id && m.IsLeader && m.LeftAt == null)
                .Select(m => m.UserId).SingleAsync());

        using var mentorClient = app.CreateAuthenticatedClient(mentorId, "mentor@example.test", "Assigned Mentor", AppRoles.Lecturer);
        var inbox = await BodyAsync<PagedResult<TeamLeaderChangeRequestDto>>(
            await mentorClient.GetAsync("/api/v1/team-leader-change-requests?status=PENDING"));
        Assert.Contains(inbox.Items, item => item.Id == pending.Id);
        if (decision == "wrong-mentor")
        {
            using var nonPrimaryMentor = app.CreateAuthenticatedClient(
                nonPrimaryMentorId, roles: [AppRoles.Lecturer]);
            Assert.Equal(HttpStatusCode.Forbidden, (await nonPrimaryMentor.PostAsJsonAsync(
                $"/api/v1/team-leader-change-requests/{pending.Id}/approve", new { })).StatusCode);
            await using var db = database.CreateContext();
            Assert.Equal("PENDING", await db.TeamLeaderChangeRequests.Where(r => r.Id == pending.Id)
                .Select(r => r.Status).SingleAsync());
            var members = await db.TeamMembers.Where(m => m.TeamId == team.Id && m.LeftAt == null
                && (m.UserId == scenario.Students[0] || m.UserId == scenario.Students[1]))
                .Select(m => new { m.UserId, m.IsLeader }).ToListAsync();
            Assert.Contains(members, member => member.UserId == scenario.Students[0] && member.IsLeader);
            Assert.Contains(members, member => member.UserId == scenario.Students[1] && !member.IsLeader);
            Assert.False(await db.AuditLogs.AnyAsync(a => a.Action == "TEAM_LEADER_CHANGE_APPROVED"
                && a.EntityId == pending.Id.ToString()));
            Assert.False(await db.Notifications.AnyAsync(n => n.RelatedEntityId == pending.Id
                && n.NotificationType == "TEAM_LEADER_CHANGE_APPROVED"));
            return;
        }
        if (decision.StartsWith("ended-"))
        {
            await using var db = database.CreateContext();
            await db.SupervisorAssignments.Where(a => a.ProjectId == pending.ProjectId)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.EndedAt, TeamDatabaseFixture.Now));
            Assert.Equal(HttpStatusCode.Conflict, (await mentorClient.PostAsJsonAsync(
                $"/api/v1/team-leader-change-requests/{pending.Id}/{decision[6..]}", new { })).StatusCode);
            Assert.Equal("PENDING", (await db.TeamLeaderChangeRequests.AsNoTracking().SingleAsync(r => r.Id == pending.Id)).Status);
            Assert.Equal(scenario.Students[0], await db.TeamMembers.Where(m => m.TeamId == team.Id && m.IsLeader)
                .Select(m => m.UserId).SingleAsync());
            return;
        }
        if (decision == "inactive-mentor")
        {
            await using var db = database.CreateContext();
            await db.Users.Where(u => u.Id == mentorId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, "INACTIVE"));
            Assert.Equal(HttpStatusCode.Forbidden, (await mentorClient.PostAsJsonAsync(
                $"/api/v1/team-leader-change-requests/{pending.Id}/approve", new { })).StatusCode);
            Assert.Equal("PENDING", await db.TeamLeaderChangeRequests.Where(r => r.Id == pending.Id)
                .Select(r => r.Status).SingleAsync());
            Assert.Equal(scenario.Students[0], await db.TeamMembers.Where(m => m.TeamId == team.Id && m.IsLeader && m.LeftAt == null)
                .Select(m => m.UserId).SingleAsync());
            return;
        }
        if (decision == "qualification-revoked")
        {
            await RevokeQualificationAsync(scenario.Students[1]);
            Assert.Equal(HttpStatusCode.Conflict, (await mentorClient.PostAsJsonAsync(
                $"/api/v1/team-leader-change-requests/{pending.Id}/approve", new { })).StatusCode);
            await using var db = database.CreateContext();
            Assert.Equal("PENDING", await db.TeamLeaderChangeRequests.Where(r => r.Id == pending.Id)
                .Select(r => r.Status).SingleAsync());
            Assert.Equal(scenario.Students[0], await db.TeamMembers.Where(m => m.TeamId == team.Id && m.IsLeader && m.LeftAt == null)
                .Select(m => m.UserId).SingleAsync());
            return;
        }
        if (decision == "target-left")
        {
            await using var db = database.CreateContext();
            await db.TeamMembers.Where(m => m.TeamId == team.Id && m.UserId == scenario.Students[1])
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.LeftAt, TeamDatabaseFixture.Now.AddMinutes(1)));
            Assert.Equal(HttpStatusCode.Conflict, (await mentorClient.PostAsJsonAsync(
                $"/api/v1/team-leader-change-requests/{pending.Id}/approve", new { })).StatusCode);
            Assert.Equal("PENDING", await db.TeamLeaderChangeRequests.Where(r => r.Id == pending.Id)
                .Select(r => r.Status).SingleAsync());
            Assert.Equal(scenario.Students[0], await db.TeamMembers.Where(m => m.TeamId == team.Id && m.IsLeader && m.LeftAt == null)
                .Select(m => m.UserId).SingleAsync());
            return;
        }
        if (decision == "current-leader-changed")
        {
            await using var db = database.CreateContext();
            await db.TeamMembers.Where(m => m.TeamId == team.Id && m.UserId == scenario.Students[0])
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.IsLeader, false));
            await db.TeamMembers.Where(m => m.TeamId == team.Id && m.UserId == scenario.Students[1])
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.IsLeader, true));
            Assert.Equal(HttpStatusCode.Conflict, (await mentorClient.PostAsJsonAsync(
                $"/api/v1/team-leader-change-requests/{pending.Id}/approve", new { })).StatusCode);
            Assert.Equal("PENDING", await db.TeamLeaderChangeRequests.Where(r => r.Id == pending.Id)
                .Select(r => r.Status).SingleAsync());
            Assert.Equal(scenario.Students[1], await db.TeamMembers.Where(m => m.TeamId == team.Id && m.IsLeader && m.LeftAt == null)
                .Select(m => m.UserId).SingleAsync());
            return;
        }
        if (decision == "concurrent-approve")
        {
            using var secondMentor = app.CreateAuthenticatedClient(mentorId, "mentor@example.test", "Assigned Mentor", AppRoles.Lecturer);
            var responses = await Task.WhenAll(
                mentorClient.PostAsJsonAsync($"/api/v1/team-leader-change-requests/{pending.Id}/approve", new { }),
                secondMentor.PostAsJsonAsync($"/api/v1/team-leader-change-requests/{pending.Id}/approve", new { }));
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            await using var db = database.CreateContext();
            Assert.Equal("APPROVED", await db.TeamLeaderChangeRequests.Where(r => r.Id == pending.Id)
                .Select(r => r.Status).SingleAsync());
            Assert.Equal(scenario.Students[1], await db.TeamMembers.Where(m => m.TeamId == team.Id && m.IsLeader && m.LeftAt == null)
                .Select(m => m.UserId).SingleAsync());
            Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == "TEAM_LEADER_CHANGE_APPROVED"
                && a.EntityId == pending.Id.ToString()));
            return;
        }
        if (decision == "audit-failure")
        {
            using var failingApp = new TeamTestFactory(database, scenario, failAuditAction: "TEAM_LEADER_CHANGE_APPROVED");
            using var failingMentor = failingApp.CreateAuthenticatedClient(mentorId, roles: [AppRoles.Lecturer]);
            Assert.Equal(HttpStatusCode.InternalServerError, (await failingMentor.PostAsJsonAsync(
                $"/api/v1/team-leader-change-requests/{pending.Id}/approve", new { })).StatusCode);
            await using var db = database.CreateContext();
            Assert.Equal("PENDING", (await db.TeamLeaderChangeRequests.SingleAsync(r => r.Id == pending.Id)).Status);
            Assert.Equal(scenario.Students[0], await db.TeamMembers.Where(m => m.TeamId == team.Id && m.IsLeader)
                .Select(m => m.UserId).SingleAsync());
            Assert.False(await db.Notifications.AnyAsync(n => n.RelatedEntityId == pending.Id
                && n.NotificationType == "TEAM_LEADER_CHANGE_APPROVED"));
            return;
        }
        if (decision == "notification-failure")
        {
            using var failingApp = new TeamTestFactory(database, scenario, customizeServices: InjectApprovedNotificationFailure);
            using var failingMentor = failingApp.CreateAuthenticatedClient(mentorId, roles: [AppRoles.Lecturer]);
            Assert.Equal(HttpStatusCode.InternalServerError, (await failingMentor.PostAsJsonAsync(
                $"/api/v1/team-leader-change-requests/{pending.Id}/approve", new { })).StatusCode);
            await using var db = database.CreateContext();
            Assert.Equal("PENDING", (await db.TeamLeaderChangeRequests.SingleAsync(r => r.Id == pending.Id)).Status);
            Assert.Equal(scenario.Students[0], await db.TeamMembers.Where(m => m.TeamId == team.Id && m.IsLeader)
                .Select(m => m.UserId).SingleAsync());
            Assert.False(await db.AuditLogs.AnyAsync(a => a.Action == "TEAM_LEADER_CHANGE_APPROVED"
                && a.EntityId == pending.Id.ToString()));
            Assert.False(await db.Notifications.AnyAsync(n => n.RelatedEntityId == pending.Id
                && n.NotificationType == "TEAM_LEADER_CHANGE_APPROVED"));
            return;
        }
        if (decision == "reject")
        {
            var rejected = await BodyAsync<TeamLeaderChangeRequestDto>(await mentorClient.PostAsJsonAsync(
                $"/api/v1/team-leader-change-requests/{pending.Id}/reject", new { message = "Declined" }));
            Assert.Equal("REJECTED", rejected.Status);
            Assert.Equal(HttpStatusCode.Conflict, (await mentorClient.PostAsJsonAsync(
                $"/api/v1/team-leader-change-requests/{pending.Id}/approve", new { })).StatusCode);
            await using var db = database.CreateContext();
            Assert.Equal(scenario.Students[0], await db.TeamMembers.Where(m => m.TeamId == team.Id && m.IsLeader)
                .Select(m => m.UserId).SingleAsync());
            var notice = await db.Notifications.Include(n => n.NotificationRecipients).SingleAsync(n =>
                n.RelatedEntityId == pending.Id && n.NotificationType == "TEAM_LEADER_CHANGE_REJECTED");
            Assert.Equal(scenario.Students[0], Assert.Single(notice.NotificationRecipients).UserId);
            var retry = await BodyAsync<TeamLeaderChangeRequestDto>(await leader.PostAsJsonAsync(
                $"/api/v1/teams/{team.Id}/leader-change-requests", new { newLeaderUserId = scenario.Students[1] }));
            Assert.NotEqual(pending.Id, retry.Id);
            return;
        }
        var approved = await BodyAsync<TeamLeaderChangeRequestDto>(await mentorClient.PostAsJsonAsync(
            $"/api/v1/team-leader-change-requests/{pending.Id}/approve", new { message = "Approved" }));
        Assert.Equal("APPROVED", approved.Status);
        var replay = await BodyAsync<TeamLeaderChangeRequestDto>(await mentorClient.PostAsJsonAsync(
            $"/api/v1/team-leader-change-requests/{pending.Id}/approve", new { }));
        Assert.Equal(approved, replay);
        await using (var noticeDb = database.CreateContext())
        {
            var notice = await noticeDb.Notifications.Include(n => n.NotificationRecipients)
                .SingleAsync(n => n.RelatedEntityType == "TEAM_LEADER_CHANGE_REQUEST"
                    && n.RelatedEntityId == pending.Id && n.NotificationType == "TEAM_LEADER_CHANGE_APPROVED");
            Assert.Equal(scenario.Students[0], Assert.Single(notice.NotificationRecipients).UserId);
        }

        await using var after = database.CreateContext();
        Assert.Equal(scenario.Students[1], await after.TeamMembers
            .Where(m => m.TeamId == team.Id && m.IsLeader && m.LeftAt == null)
            .Select(m => m.UserId).SingleAsync());
        Assert.Contains("TEAM_LEADER_CHANGE_APPROVED", await after.AuditLogs
            .Where(a => a.EntityId == pending.Id.ToString()).Select(a => a.Action).ToArrayAsync());
    }

    private void InjectApprovedNotificationFailure(IServiceCollection services)
    {
        services.RemoveAll<DbContextOptions<AipmsDbContext>>();
        services.AddDbContext<AipmsDbContext>(options => options.UseSqlServer(database.ConnectionString)
            .AddInterceptors(new FailApprovedNotificationSave()));
    }

    private sealed class FailApprovedNotificationSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<M.Notification>().Any(entry => entry.State == EntityState.Added
                && entry.Entity.NotificationType == "TEAM_LEADER_CHANGE_APPROVED"))
                throw new InvalidOperationException("Injected approved leader-change notification failure");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
