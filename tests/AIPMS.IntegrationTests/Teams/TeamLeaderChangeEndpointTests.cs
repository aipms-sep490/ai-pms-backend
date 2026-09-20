using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests.Teams;

public sealed partial class TeamEndpointTests
{
    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("ended-approve")]
    [InlineData("ended-reject")]
    [InlineData("audit-failure")]
    public async Task Assigned_mentor_must_approve_leader_change_before_membership_changes(string decision)
    {
        var scenario = await database.SeedAsync();
        using var app = new TeamTestFactory(database, scenario);
        using var leader = app.CreateAuthenticatedClient(scenario.Students[0]);
        using var member = app.CreateAuthenticatedClient(scenario.Students[1]);
        var team = await CreateAsync(leader, scenario);
        var invitation = await InviteAsync(leader, team.Id, scenario.Students[1]);
        await BodyAsync<TeamDto>(await AcceptAsync(member, invitation.Id));

        long mentorId;
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
            await db.SaveChangesAsync();
            mentorId = mentor.Id;
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
}
