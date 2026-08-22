using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using AIPMS.Api.Controllers;
using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Task = System.Threading.Tasks.Task;
namespace AIPMS.IntegrationTests;
internal sealed record SendRequestPayload(long SupervisorId, string? RequestMessage);
internal sealed record RejectRequestPayload(string? ResponseMessage);
internal static class TestCurrentUser
{
    private static AipmsWebApplicationFactory? Factory { get; set; }
    public static HttpClient? CurrentClient { get; private set; }
    public static void Configure(AipmsWebApplicationFactory factory) => Factory = factory;
    public static void SetUser(long? id, string? email = null, string? role = null)
    {
        CurrentClient = id.HasValue
            ? Factory!.CreateAuthenticatedClient(id.Value, email ?? "user@aipms.test", "Supervisor Test User", role ?? "LECTURER")
            : Factory!.CreateClient();
    }
}
public sealed class SupervisorManagementIntegrationTests : IClassFixture<AipmsWebApplicationFactory>
{
    private readonly HttpClient _initialClient;
    private HttpClient _client => TestCurrentUser.CurrentClient ?? _initialClient;
    private readonly AipmsWebApplicationFactory _factory;
    private long _studentUserId;
    private long _lecturerAUserId;
    private long _lecturerBUserId;
    private long _profileAId;
    private long _profileBId;
    private long _project1Id;
    private long _project2Id;
    private long _project3Id;

    public SupervisorManagementIntegrationTests(AipmsWebApplicationFactory factory)
    {
        _factory = factory;
        _initialClient = factory.CreateClient();
        TestCurrentUser.Configure(factory);
    }

    private async Task SeedDatabaseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AipmsDbContext>();

        // Clear BE-08 dependants before the core supervisor/project seed is reset.
        db.NotificationRecipients.RemoveRange(db.NotificationRecipients);
        db.Notifications.RemoveRange(db.Notifications);
        db.Files.RemoveRange(db.Files);
        db.SupervisorFeedbacks.RemoveRange(db.SupervisorFeedbacks);
        db.DeliverableVersions.RemoveRange(db.DeliverableVersions);
        db.Deliverables.RemoveRange(db.Deliverables);
        db.SupervisorAssignments.RemoveRange(db.SupervisorAssignments);
        db.SupervisorRequests.RemoveRange(db.SupervisorRequests);
        db.Projects.RemoveRange(db.Projects);
        db.TeamMembers.RemoveRange(db.TeamMembers);
        db.Teams.RemoveRange(db.Teams);
        db.AcademicSemesters.RemoveRange(db.AcademicSemesters);
        db.Organizations.RemoveRange(db.Organizations);
        db.SupervisorExpertises.RemoveRange(db.SupervisorExpertises);
        db.SupervisorProfiles.RemoveRange(db.SupervisorProfiles);
        db.Users.RemoveRange(db.Users);
        await db.SaveChangesAsync();

        // 1. Seed Organization
        var org = new Organization { Code = "FPT", Name = "FPT University", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        await db.Organizations.AddAsync(org);
        await db.SaveChangesAsync();

        // 2. Seed Academic Semester
        var semester = new AcademicSemester
        {
            OrganizationId = org.Id,
            Code = "FA26",
            Name = "Fall 2026",
            StartDate = new DateOnly(2026, 9, 1),
            EndDate = new DateOnly(2026, 12, 31),
            Status = "ACTIVE",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await db.AcademicSemesters.AddAsync(semester);
        await db.SaveChangesAsync();

        var semester2 = new AcademicSemester
        {
            OrganizationId = org.Id,
            Code = "SP26",
            Name = "Spring 2026",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 4, 30),
            Status = "ACTIVE",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var semester3 = new AcademicSemester
        {
            OrganizationId = org.Id,
            Code = "SU26",
            Name = "Summer 2026",
            StartDate = new DateOnly(2026, 5, 1),
            EndDate = new DateOnly(2026, 8, 31),
            Status = "ACTIVE",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await db.AcademicSemesters.AddRangeAsync(semester2, semester3);
        await db.SaveChangesAsync();

        // 3. Seed Users
        var student = new User { Email = "student@aipms.com", PasswordHash = "hash", FullName = "Student One", Status = "ACTIVE", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var lecturerA = new User { Email = "lecturerA@aipms.com", PasswordHash = "hash", FullName = "Lecturer A", Status = "ACTIVE", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var lecturerB = new User { Email = "lecturerB@aipms.com", PasswordHash = "hash", FullName = "Lecturer B", Status = "ACTIVE", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        await db.Users.AddRangeAsync(student, lecturerA, lecturerB);
        await db.SaveChangesAsync();

        _studentUserId = student.Id;
        _lecturerAUserId = lecturerA.Id;
        _lecturerBUserId = lecturerB.Id;

        // 4. Seed Supervisor Profiles
        var profileA = new SupervisorProfile { UserId = lecturerA.Id, Bio = "AI Expert", MaxActiveProjects = 2, IsAvailable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var profileB = new SupervisorProfile { UserId = lecturerB.Id, Bio = "Web Expert", MaxActiveProjects = 2, IsAvailable = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        await db.SupervisorProfiles.AddRangeAsync(profileA, profileB);
        await db.SaveChangesAsync();

        _profileAId = profileA.Id;
        _profileBId = profileB.Id;

        // 5. Seed Teams
        var team1 = new Team { AcademicSemesterId = semester.Id, Code = "T01", Name = "Team 1", Status = "FORMING", CreatedBy = student.Id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var team2 = new Team { AcademicSemesterId = semester2.Id, Code = "T02", Name = "Team 2", Status = "FORMING", CreatedBy = student.Id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var team3 = new Team { AcademicSemesterId = semester3.Id, Code = "T03", Name = "Team 3", Status = "FORMING", CreatedBy = student.Id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        await db.Teams.AddRangeAsync(team1, team2, team3);
        await db.SaveChangesAsync();

        await db.TeamMembers.AddRangeAsync(
            new TeamMember { TeamId = team1.Id, AcademicSemesterId = semester.Id, UserId = student.Id, IsLeader = true, JoinedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new TeamMember { TeamId = team2.Id, AcademicSemesterId = semester2.Id, UserId = student.Id, IsLeader = true, JoinedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new TeamMember { TeamId = team3.Id, AcademicSemesterId = semester3.Id, UserId = student.Id, IsLeader = true, JoinedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        // 6. Seed Projects
        var project1 = new Project { TeamId = team1.Id, Code = "PRJ001", Title = "AI Project 1", Status = "APPROVED", CreatedBy = student.Id, RegisteredAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var project2 = new Project { TeamId = team2.Id, Code = "PRJ002", Title = "AI Project 2", Status = "APPROVED", CreatedBy = student.Id, RegisteredAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var project3 = new Project { TeamId = team3.Id, Code = "PRJ003", Title = "AI Project 3", Status = "APPROVED", CreatedBy = student.Id, RegisteredAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        await db.Projects.AddRangeAsync(project1, project2, project3);
        await db.SaveChangesAsync();

        _project1Id = project1.Id;
        _project2Id = project2.Id;
        _project3Id = project3.Id;
    }

    [Fact]
    public async Task Flow1_SendAndAcceptRequest_CreatesActiveAssignment()
    {
        await SeedDatabaseAsync();

        TestCurrentUser.SetUser(_studentUserId, "student@aipms.com", "STUDENT");
        var payload = new SendRequestPayload(_profileAId, "Please supervise our project");
        var response = await _client.PostAsJsonAsync($"/api/v1/projects/{_project1Id}/supervisor-requests", payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var requestDto = await response.Content.ReadFromJsonAsync<SupervisorRequestDto>();
        Assert.NotNull(requestDto);
        Assert.Equal("PENDING", requestDto.Status);
        Assert.Equal(_profileAId, requestDto.SupervisorProfileId);

        TestCurrentUser.SetUser(_lecturerAUserId, "lecturerA@aipms.com", "LECTURER");
        var acceptResponse = await _client.PostAsync($"/api/supervisor-requests/{requestDto.Id}/accept", null);

        Assert.Equal(HttpStatusCode.OK, acceptResponse.StatusCode);

        TestCurrentUser.SetUser(_studentUserId, "student@aipms.com", "STUDENT");
        var supervisorResponse = await _client.GetAsync($"/api/v1/projects/{_project1Id}/supervisor");
        Assert.Equal(HttpStatusCode.OK, supervisorResponse.StatusCode);
        var supervisorDto = await supervisorResponse.Content.ReadFromJsonAsync<SupervisorDto>();
        Assert.NotNull(supervisorDto);
        Assert.Equal(_profileAId, supervisorDto.Id);
    }

    [Fact]
    public async Task Flow2_RejectRequest_DoesNotCreateAssignment()
    {
        await SeedDatabaseAsync();

        TestCurrentUser.SetUser(_studentUserId, "student@aipms.com", "STUDENT");
        var payload = new SendRequestPayload(_profileAId, "Please supervise our project");
        var response = await _client.PostAsJsonAsync($"/api/v1/projects/{_project1Id}/supervisor-requests", payload);
        var requestDto = await response.Content.ReadFromJsonAsync<SupervisorRequestDto>();

        TestCurrentUser.SetUser(_lecturerAUserId, "lecturerA@aipms.com", "LECTURER");
        var rejectPayload = new RejectRequestPayload("No time");
        var rejectResponse = await _client.PostAsJsonAsync($"/api/supervisor-requests/{requestDto!.Id}/reject", rejectPayload);

        Assert.Equal(HttpStatusCode.OK, rejectResponse.StatusCode);

        TestCurrentUser.SetUser(_studentUserId, "student@aipms.com", "STUDENT");
        var supervisorResponse = await _client.GetAsync($"/api/v1/projects/{_project1Id}/supervisor");
        Assert.Equal(HttpStatusCode.NoContent, supervisorResponse.StatusCode);
    }

    [Fact]
    public async Task Flow3_EndAssignment_SetsEndedAt()
    {
        await SeedDatabaseAsync();

        TestCurrentUser.SetUser(_studentUserId, "student@aipms.com", "STUDENT");
        var payload = new SendRequestPayload(_profileAId, "supervise");
        var response = await _client.PostAsJsonAsync($"/api/v1/projects/{_project1Id}/supervisor-requests", payload);
        var requestDto = await response.Content.ReadFromJsonAsync<SupervisorRequestDto>();

        TestCurrentUser.SetUser(_lecturerAUserId, "lecturerA@aipms.com", "LECTURER");
        await _client.PostAsync($"/api/supervisor-requests/{requestDto!.Id}/accept", null);

        long assignmentId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AipmsDbContext>();
            var assignment = await db.SupervisorAssignments.FirstAsync(a => a.ProjectId == _project1Id);
            assignmentId = assignment.Id;
            Assert.Null(assignment.EndedAt);
        }

        TestCurrentUser.SetUser(_lecturerAUserId, "lecturerA@aipms.com", "LECTURER");
        var endResponse = await _client.PostAsync($"/api/supervisor-assignments/{assignmentId}/end", null);
        Assert.Equal(HttpStatusCode.OK, endResponse.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AipmsDbContext>();
            var assignment = await db.SupervisorAssignments.FindAsync(assignmentId);
            Assert.NotNull(assignment!.EndedAt);
        }
    }

    [Fact]
    public async Task Flow4_WrongSupervisor_FailsWithForbidden()
    {
        await SeedDatabaseAsync();

        TestCurrentUser.SetUser(_studentUserId, "student@aipms.com", "STUDENT");
        var payload = new SendRequestPayload(_profileAId, "supervise");
        var response = await _client.PostAsJsonAsync($"/api/v1/projects/{_project1Id}/supervisor-requests", payload);
        var requestDto = await response.Content.ReadFromJsonAsync<SupervisorRequestDto>();

        TestCurrentUser.SetUser(_lecturerBUserId, "lecturerB@aipms.com", "LECTURER");
        var acceptResponse = await _client.PostAsync($"/api/supervisor-requests/{requestDto!.Id}/accept", null);

        Assert.Equal(HttpStatusCode.Forbidden, acceptResponse.StatusCode);
    }

    [Fact]
    public async Task Flow5_DuplicatePending_FailsWithConflict()
    {
        await SeedDatabaseAsync();

        TestCurrentUser.SetUser(_studentUserId, "student@aipms.com", "STUDENT");
        var payload = new SendRequestPayload(_profileAId, "supervise");

        var response1 = await _client.PostAsJsonAsync($"/api/v1/projects/{_project1Id}/supervisor-requests", payload);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

        var response2 = await _client.PostAsJsonAsync($"/api/v1/projects/{_project1Id}/supervisor-requests", payload);
        Assert.Equal(HttpStatusCode.Conflict, response2.StatusCode);
    }

    [Fact]
    public async Task Flow6_MaxActiveProjects_FailsWithConflict()
    {
        await SeedDatabaseAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AipmsDbContext>();

            var req1 = new SupervisorRequest { ProjectId = _project1Id, SupervisorProfileId = _profileAId, RequestedBy = _studentUserId, Status = "ACCEPTED", RequestedAt = DateTime.UtcNow, RespondedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            await db.SupervisorRequests.AddAsync(req1);
            await db.SaveChangesAsync();

            var a1 = new SupervisorAssignment { ProjectId = _project1Id, SupervisorProfileId = _profileAId, SupervisorRequestId = req1.Id, IsPrimary = true, AssignedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            await db.SupervisorAssignments.AddAsync(a1);

            var req2 = new SupervisorRequest { ProjectId = _project2Id, SupervisorProfileId = _profileAId, RequestedBy = _studentUserId, Status = "ACCEPTED", RequestedAt = DateTime.UtcNow, RespondedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            await db.SupervisorRequests.AddAsync(req2);
            await db.SaveChangesAsync();

            var a2 = new SupervisorAssignment { ProjectId = _project2Id, SupervisorProfileId = _profileAId, SupervisorRequestId = req2.Id, IsPrimary = true, AssignedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            await db.SupervisorAssignments.AddAsync(a2);

            await db.SaveChangesAsync();
        }

        TestCurrentUser.SetUser(_studentUserId, "student@aipms.com", "STUDENT");
        var payload = new SendRequestPayload(_profileAId, "supervise project 3");
        var response = await _client.PostAsJsonAsync($"/api/v1/projects/{_project3Id}/supervisor-requests", payload);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
