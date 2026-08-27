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

        db.SupervisorAssignments.RemoveRange(db.SupervisorAssignments);
        db.SupervisorRequests.RemoveRange(db.SupervisorRequests);
        db.Milestones.RemoveRange(db.Milestones);
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
        await db.SupervisorExpertises.AddRangeAsync(
            new SupervisorExpertise { SupervisorProfileId = profileA.Id, ExpertiseName = "AI", ProficiencyLevel = "EXPERT", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new SupervisorExpertise { SupervisorProfileId = profileB.Id, ExpertiseName = "Web", ProficiencyLevel = "ADVANCED", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        // 5. Seed Teams
        var team1 = new Team { AcademicSemesterId = semester.Id, Code = "T01", Name = "Team 1", Status = "FORMING", CreatedBy = student.Id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var team2 = new Team { AcademicSemesterId = semester.Id, Code = "T02", Name = "Team 2", Status = "FORMING", CreatedBy = student.Id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var team3 = new Team { AcademicSemesterId = semester.Id, Code = "T03", Name = "Team 3", Status = "FORMING", CreatedBy = student.Id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        await db.Teams.AddRangeAsync(team1, team2, team3);
        await db.SaveChangesAsync();

        await db.TeamMembers.AddAsync(
            new TeamMember { TeamId = team1.Id, AcademicSemesterId = semester.Id, UserId = student.Id, IsLeader = true, JoinedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
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

        // Retrying accept is idempotent and initializes the project workspace once.
        TestCurrentUser.SetUser(_lecturerAUserId, "lecturerA@aipms.com", "LECTURER");
        var retryResponse = await _client.PostAsync($"/api/supervisor-requests/{requestDto.Id}/accept", null);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AipmsDbContext>();
        Assert.Equal("ACTIVE", (await db.Projects.FindAsync(_project1Id))!.Status);
        Assert.Single(await db.SupervisorAssignments.Where(x => x.SupervisorRequestId == requestDto.Id).ToListAsync());
        Assert.Single(await db.Milestones.Where(x => x.ProjectId == _project1Id && x.Title == "Project Workspace").ToListAsync());
    }

    [Fact]
    public async Task Candidates_UseDeterministicCapacityAndIncludeExpertise_WhenAiUnavailable()
    {
        await SeedDatabaseAsync();
        TestCurrentUser.SetUser(_studentUserId, "student@aipms.com", "STUDENT");

        var response = await _client.GetAsync($"/api/v1/projects/{_project1Id}/supervisor-candidates?expertise=AI");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var candidates = await response.Content.ReadFromJsonAsync<List<SupervisorCandidateDto>>();
        var candidate = Assert.Single(candidates!);
        Assert.Equal(_profileAId, candidate.SupervisorId);
        Assert.Equal(0, candidate.CurrentActiveProjects);
        Assert.Equal(2, candidate.AvailableCapacity);
        Assert.Contains(candidate.Expertises, x => x.ExpertiseName == "AI");
        Assert.False(candidate.AiAvailable);
        Assert.Null(candidate.AiRationale);
    }

    [Fact]
    public async Task SupervisorListDetailAndProfileUpdates_WorkThroughEndpoints()
    {
        await SeedDatabaseAsync();
        TestCurrentUser.SetUser(_lecturerAUserId, "lecturerA@aipms.com", "LECTURER");

        var listResponse = await _client.GetAsync("/api/supervisors?pageNumber=1&pageSize=1&expertise=AI");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content.ReadFromJsonAsync<AIPMS.Application.Common.Models.PagedResult<SupervisorDto>>();
        Assert.NotNull(page);
        Assert.Single(page.Items);
        Assert.Equal(1, page.PageSize);

        var detailResponse = await _client.GetAsync($"/api/supervisors/{_profileAId}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<SupervisorDetailDto>();
        Assert.Equal(_profileAId, detail!.Id);
        Assert.Contains(detail.Expertises, x => x.ExpertiseName == "AI");

        var profileResponse = await _client.PutAsJsonAsync("/api/supervisors/me/profile",
            new { Bio = "Updated bio", MaxActiveProjects = 4, IsAvailable = true });
        Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);
        var expertiseResponse = await _client.PutAsJsonAsync("/api/supervisors/me/expertise",
            new { Expertises = new[] { new { ExpertiseName = "Data Science", ProficiencyLevel = "EXPERT" } } });
        Assert.Equal(HttpStatusCode.OK, expertiseResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AipmsDbContext>();
        var stored = await db.SupervisorProfiles.FindAsync(_profileAId);
        Assert.Equal("Updated bio", stored!.Bio);
        Assert.Equal(4, stored.MaxActiveProjects);
        var storedExpertise = await db.SupervisorExpertises.SingleAsync(x => x.SupervisorProfileId == _profileAId);
        Assert.Equal("Data Science", storedExpertise.ExpertiseName);
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

            // Grant platform project access so this capacity test can target project 3,
            // whose team intentionally has no seeded member in this fixture.
            var staffRoleId = await db.Roles.Where(x => x.Code == "DEPARTMENT_STAFF").Select(x => x.Id).SingleAsync();
            await db.UserRoles.AddAsync(new UserRole
            {
                UserId = _studentUserId,
                RoleId = staffRoleId,
                AssignedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        TestCurrentUser.SetUser(_studentUserId, "student@aipms.com", "STUDENT");
        var payload = new SendRequestPayload(_profileAId, "supervise project 3");
        var response = await _client.PostAsJsonAsync($"/api/v1/projects/{_project3Id}/supervisor-requests", payload);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SendRequest_ProjectNotApproved_FailsWithConflict()
    {
        await SeedDatabaseAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AipmsDbContext>();
            var project = await db.Projects.FindAsync(_project1Id);
            project!.Status = "SUBMITTED";
            await db.SaveChangesAsync();
        }
        TestCurrentUser.SetUser(_studentUserId, "student@aipms.com", "STUDENT");

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{_project1Id}/supervisor-requests",
            new SendRequestPayload(_profileAId, "supervise"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CancelRequest_OnlySenderCanCancel_AndRetainsActorAndTime()
    {
        await SeedDatabaseAsync();
        TestCurrentUser.SetUser(_studentUserId, "student@aipms.com", "STUDENT");
        var send = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{_project1Id}/supervisor-requests",
            new SendRequestPayload(_profileAId, "supervise"));
        var request = await send.Content.ReadFromJsonAsync<SupervisorRequestDto>();

        TestCurrentUser.SetUser(_lecturerBUserId, "lecturerB@aipms.com", "LECTURER");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _client.PostAsync($"/api/supervisor-requests/{request!.Id}/cancel", null)).StatusCode);

        TestCurrentUser.SetUser(_studentUserId, "student@aipms.com", "STUDENT");
        Assert.Equal(HttpStatusCode.OK,
            (await _client.PostAsync($"/api/supervisor-requests/{request.Id}/cancel", null)).StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AipmsDbContext>();
        var stored = await db.SupervisorRequests.FindAsync(request.Id);
        Assert.Equal("CANCELLED", stored!.Status);
        Assert.Equal(_studentUserId, stored.RequestedBy);
        Assert.NotNull(stored.RespondedAt);
    }

    [Fact]
    public async Task ConcurrentAccepts_RecheckCapacity_AllowsOnlyOneAssignment()
    {
        await SeedDatabaseAsync();
        long request1Id;
        long request2Id;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AipmsDbContext>();
            (await db.SupervisorProfiles.FindAsync(_profileAId))!.MaxActiveProjects = 1;
            var now = DateTime.UtcNow;
            var request1 = new SupervisorRequest
            {
                ProjectId = _project1Id, SupervisorProfileId = _profileAId, RequestedBy = _studentUserId,
                Status = "PENDING", RequestedAt = now, CreatedAt = now, UpdatedAt = now
            };
            var request2 = new SupervisorRequest
            {
                ProjectId = _project2Id, SupervisorProfileId = _profileAId, RequestedBy = _studentUserId,
                Status = "PENDING", RequestedAt = now, CreatedAt = now, UpdatedAt = now
            };
            await db.SupervisorRequests.AddRangeAsync(request1, request2);
            await db.SaveChangesAsync();
            request1Id = request1.Id;
            request2Id = request2.Id;
        }
        TestCurrentUser.SetUser(_lecturerAUserId, "lecturerA@aipms.com", "LECTURER");

        var responses = await Task.WhenAll(
            _client.PostAsync($"/api/supervisor-requests/{request1Id}/accept", null),
            _client.PostAsync($"/api/supervisor-requests/{request2Id}/accept", null));

        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        using var verificationScope = _factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<AipmsDbContext>();
        Assert.Equal(1, await verificationDb.SupervisorAssignments.CountAsync(
            x => x.SupervisorProfileId == _profileAId && x.EndedAt == null));
    }
}
