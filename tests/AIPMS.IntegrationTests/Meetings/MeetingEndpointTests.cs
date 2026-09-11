using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Meetings.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.IntegrationTests.Supervisors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Meetings;

public sealed class MeetingEndpointTests(SupervisorDatabaseFixture database) : IClassFixture<SupervisorDatabaseFixture>
{
    private static readonly DateTime Now = new(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);

    private sealed record Scenario(
        SupervisorScenario Accounts,
        long ProjectId,
        long TeamId,
        long MemberId,
        long AssignmentId);

    private sealed class Factory(SupervisorDatabaseFixture database) : AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = database.ConnectionString
            }));
        }
    }

    private async Task<Scenario> SeedAsync()
    {
        var s = await database.SeedAsync();
        await using var db = database.CreateContext();
        var dept = await db.Departments.FindAsync(s.DepartmentId);
        var semester = new M.AcademicSemester
        {
            OrganizationId = dept!.OrganizationId,
            Code = Guid.NewGuid().ToString("N"),
            Name = "Semester",
            Status = "ACTIVE",
            StartDate = DateOnly.FromDateTime(Now.AddDays(-30)),
            EndDate = DateOnly.FromDateTime(Now.AddDays(30))
        };
        var studentRole = await db.Roles.SingleAsync(r => r.Code == AppRoles.Student);
        var member = new M.User
        {
            DepartmentId = s.DepartmentId,
            Email = $"{Guid.NewGuid():N}@test.local",
            FullName = "Meeting Team Member",
            PasswordHash = "unused",
            Status = "ACTIVE",
            UserRoleUsers = [new() { RoleId = studentRole.Id }]
        };
        db.Users.Add(member);
        db.AcademicSemesters.Add(semester);
        await db.SaveChangesAsync();

        var project = new M.Project
        {
            Code = Guid.NewGuid().ToString("N"),
            Title = "Meeting Test Project",
            Status = "ACTIVE",
            CreatedBy = s.Student,
            Team = new M.Team
            {
                Code = Guid.NewGuid().ToString("N"),
                Name = "Meeting Team",
                AcademicSemesterId = semester.Id,
                CreatedBy = s.Student,
                Status = "ELIGIBLE",
                TeamMembers =
                [
                    new() { AcademicSemesterId = semester.Id, UserId = s.Student, IsLeader = true },
                    new() { AcademicSemesterId = semester.Id, UserId = member.Id, IsLeader = false }
                ]
            },
            ProjectMajors = [new() { Major = new() { DepartmentId = s.DepartmentId, Code = Guid.NewGuid().ToString("N"), Name = "SE", IsActive = true } }]
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var request = new M.SupervisorRequest
        {
            ProjectId = project.Id,
            SupervisorProfileId = s.ProfileId,
            RequestedBy = s.Student,
            Status = "ACCEPTED",
            RequestedAt = Now.AddDays(-1)
        };
        db.SupervisorRequests.Add(request);
        await db.SaveChangesAsync();

        var assignment = new M.SupervisorAssignment
        {
            ProjectId = project.Id,
            SupervisorProfileId = s.ProfileId,
            SupervisorRequestId = request.Id,
            IsPrimary = true,
            AssignedAt = Now.AddDays(-1)
        };
        db.SupervisorAssignments.Add(assignment);
        await db.SaveChangesAsync();

        return new Scenario(s, project.Id, project.TeamId, member.Id, assignment.Id);
    }

    [Fact]
    public async Task MeetingLifecycle_LeaderSchedules_UpdateNotes_SupervisorFeedbacks_Cancel()
    {
        var s = await SeedAsync();
        using var app = new Factory(database);
        using var leaderClient = app.CreateAuthenticatedClient(s.Accounts.Student, roles: [AppRoles.Student]);
        using var memberClient = app.CreateAuthenticatedClient(s.MemberId, roles: [AppRoles.Student]);
        using var supervisorClient = app.CreateAuthenticatedClient(s.Accounts.Lecturer, roles: [AppRoles.Lecturer]);
        using var otherSupervisorClient = app.CreateAuthenticatedClient(s.Accounts.OtherLecturer, roles: [AppRoles.Lecturer]);

        // 1. Leader schedules meeting
        var createRequest = new CreateMeetingRequest(
            "Sprint Planning Meeting",
            "Discuss sprint backlog and task assignments",
            Now.AddDays(1),
            Now.AddDays(1).AddHours(1),
            "Room C201",
            "https://meet.google.com/xyz",
            new[] { s.MemberId, s.Accounts.Lecturer });

        var createResponse = await leaderClient.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/meetings", createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<MeetingDto>();
        Assert.NotNull(created);
        Assert.Equal("SCHEDULED", created.Status);
        Assert.Equal(3, created.ParticipantCount); // leader + member + lecturer

        // 2. Leader updates meeting details
        var updateRequest = new UpdateMeetingRequest(
            "Sprint Planning & Design Review",
            "Updated agenda with architecture discussion",
            Now.AddDays(1),
            Now.AddDays(1).AddHours(2),
            "Room C202",
            "https://meet.google.com/xyz");

        var updateResponse = await leaderClient.PutAsJsonAsync($"/api/v1/meetings/{created.Id}", updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<MeetingDto>();
        Assert.NotNull(updated);
        Assert.Equal("Sprint Planning & Design Review", updated.Title);

        // 3. Update meeting notes and mark attendance
        var notesRequest = new UpdateMeetingNotesRequest(
            "Meeting conducted. All user stories estimated.",
            "SCHEDULED",
            new[]
            {
                new ParticipantAttendanceUpdate(s.Accounts.Student, "ATTENDED"),
                new ParticipantAttendanceUpdate(s.MemberId, "ATTENDED"),
                new ParticipantAttendanceUpdate(s.Accounts.Lecturer, "ATTENDED")
            });

        var notesResponse = await leaderClient.PutAsJsonAsync($"/api/v1/meetings/{created.Id}/notes", notesRequest);
        Assert.Equal(HttpStatusCode.OK, notesResponse.StatusCode);
        var withNotes = await notesResponse.Content.ReadFromJsonAsync<MeetingDto>();
        Assert.NotNull(withNotes);
        Assert.Equal("Meeting conducted. All user stories estimated.", withNotes.MeetingNotes);

        // 4. Assigned supervisor adds feedback
        var feedbackRequest = new AddMeetingFeedbackRequest("Clear agenda and well-organized meeting.");
        var feedbackResponse = await supervisorClient.PostAsJsonAsync($"/api/v1/meetings/{created.Id}/feedback", feedbackRequest);
        Assert.Equal(HttpStatusCode.Created, feedbackResponse.StatusCode);
        var feedback = await feedbackResponse.Content.ReadFromJsonAsync<MeetingFeedbackDto>();
        Assert.NotNull(feedback);
        Assert.Equal("Clear agenda and well-organized meeting.", feedback.FeedbackText);

        // 5. Unrelated supervisor feedback -> 403 Forbidden
        var otherFeedbackResponse = await otherSupervisorClient.PostAsJsonAsync(
            $"/api/v1/meetings/{created.Id}/feedback", new AddMeetingFeedbackRequest("Unauthorized feedback"));
        Assert.Equal(HttpStatusCode.Forbidden, otherFeedbackResponse.StatusCode);

        // 6. Cancel meeting
        var cancelResponse = await leaderClient.PostAsync($"/api/v1/meetings/{created.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        var cancelled = await cancelResponse.Content.ReadFromJsonAsync<MeetingDto>();
        Assert.NotNull(cancelled);
        Assert.Equal("CANCELLED", cancelled.Status);

        // 7. Update on cancelled meeting -> 409 Conflict
        var postCancelUpdate = await leaderClient.PutAsJsonAsync($"/api/v1/meetings/{created.Id}", updateRequest);
        Assert.Equal(HttpStatusCode.Conflict, postCancelUpdate.StatusCode);
    }

    [Fact]
    public async Task Meeting_OutsideParticipant_Rejects_409()
    {
        var s = await SeedAsync();
        using var app = new Factory(database);
        using var leaderClient = app.CreateAuthenticatedClient(s.Accounts.Student, roles: [AppRoles.Student]);

        var createRequest = new CreateMeetingRequest(
            "Invalid Meeting",
            null,
            Now.AddDays(1),
            null,
            null,
            null,
            new[] { s.Accounts.OutsideStaff }); // Outside staff is not in project

        var response = await leaderClient.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/meetings", createRequest);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Meeting_OutsideUser_CannotViewOrModify_403()
    {
        var s = await SeedAsync();
        using var app = new Factory(database);
        using var leaderClient = app.CreateAuthenticatedClient(s.Accounts.Student, roles: [AppRoles.Student]);
        using var outsideClient = app.CreateAuthenticatedClient(s.Accounts.OutsideStaff, roles: [AppRoles.DepartmentStaff]);

        var createRequest = new CreateMeetingRequest(
            "Project Meeting", null, Now.AddDays(1), null, null, null, null);

        var createResponse = await leaderClient.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/meetings", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<MeetingDto>();
        Assert.NotNull(created);

        // Outside user attempts GET
        var getResponse = await outsideClient.GetAsync($"/api/v1/meetings/{created.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, getResponse.StatusCode);

        // Outside user attempts PUT
        var putResponse = await outsideClient.PutAsJsonAsync(
            $"/api/v1/meetings/{created.Id}", new UpdateMeetingRequest("Hacked", null, Now.AddDays(1), null, null, null));
        Assert.Equal(HttpStatusCode.Forbidden, putResponse.StatusCode);
    }
}
