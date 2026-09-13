using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.ProgressReports.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.IntegrationTests.Supervisors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.ProgressReports;

public sealed class ProgressReportEndpointTests(SupervisorDatabaseFixture database) : IClassFixture<SupervisorDatabaseFixture>
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
            FullName = "Team Member",
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
            Title = "Progress Report Test Project",
            Status = "ACTIVE",
            CreatedBy = s.Student,
            Team = new M.Team
            {
                Code = Guid.NewGuid().ToString("N"),
                Name = "Report Team",
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

        var period = new M.ProjectPeriod
        {
            AcademicSemesterId = semester.Id,
            Code = "EXEC-" + Guid.NewGuid().ToString("N")[..8],
            Name = "Execution",
            PeriodType = "EXECUTION",
            Status = "ACTIVE",
            StartAt = Now.AddDays(-10),
            EndAt = Now.AddDays(10)
        };
        db.ProjectPeriods.Add(period);
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
    public async Task ReportSubmitLifecycle_LeaderSubmits_ImmutabilityEnforced_SupervisorFeedbacks()
    {
        var s = await SeedAsync();
        using var app = new Factory(database);
        using var leaderClient = app.CreateAuthenticatedClient(s.Accounts.Student, roles: [AppRoles.Student]);
        using var memberClient = app.CreateAuthenticatedClient(s.MemberId, roles: [AppRoles.Student]);
        using var supervisorClient = app.CreateAuthenticatedClient(s.Accounts.Lecturer, roles: [AppRoles.Lecturer]);
        using var otherSupervisorClient = app.CreateAuthenticatedClient(s.Accounts.OtherLecturer, roles: [AppRoles.Lecturer]);

        // 1. Leader creates draft report
        var createRequest = new CreateProgressReportRequest(
            "WEEKLY",
            DateOnly.FromDateTime(Now.AddDays(-7)),
            DateOnly.FromDateTime(Now),
            "Initial Draft Summary",
            "Module 1 completed",
            "Module 2 planned",
            "No major risks");

        var createResponse = await leaderClient.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/progress-reports", createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<ProgressReportDto>();
        Assert.NotNull(created);
        Assert.Equal("DRAFT", created.Status);

        // 2. Member updates draft report
        var updateRequest = new UpdateProgressReportRequest(
            "Updated Draft Summary by Member",
            "Module 1 & 2 completed",
            "Module 3 planned",
            "API latency risk");

        var updateResponse = await memberClient.PutAsJsonAsync($"/api/v1/progress-reports/{created.Id}", updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<ProgressReportDto>();
        Assert.NotNull(updated);
        Assert.Equal("Updated Draft Summary by Member", updated.Summary);

        // 3. Member (non-leader) attempts to submit -> 403 Forbidden
        var memberSubmitResponse = await memberClient.PostAsync($"/api/v1/progress-reports/{created.Id}/submit", null);
        Assert.Equal(HttpStatusCode.Forbidden, memberSubmitResponse.StatusCode);

        // 4. Leader submits report -> 200 OK
        var leaderSubmitResponse = await leaderClient.PostAsync($"/api/v1/progress-reports/{created.Id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, leaderSubmitResponse.StatusCode);
        var submitted = await leaderSubmitResponse.Content.ReadFromJsonAsync<ProgressReportDto>();
        Assert.NotNull(submitted);
        Assert.Equal("SUBMITTED", submitted.Status);
        Assert.NotNull(submitted.SubmittedAt);

        // 5. Subsequent update attempt on submitted report -> 409 Conflict (Immutability enforced!)
        var postSubmitUpdate = await leaderClient.PutAsJsonAsync($"/api/v1/progress-reports/{created.Id}", updateRequest);
        Assert.Equal(HttpStatusCode.Conflict, postSubmitUpdate.StatusCode);

        // 6. Assigned supervisor provides feedback -> 201 Created
        var feedbackRequest = new AddProgressReportFeedbackRequest("Good progress, keep it up.");
        var feedbackResponse = await supervisorClient.PostAsJsonAsync($"/api/v1/progress-reports/{created.Id}/feedback", feedbackRequest);
        Assert.Equal(HttpStatusCode.Created, feedbackResponse.StatusCode);
        var feedback = await feedbackResponse.Content.ReadFromJsonAsync<ProgressReportFeedbackDto>();
        Assert.NotNull(feedback);
        Assert.Equal("Good progress, keep it up.", feedback.FeedbackText);

        // 7. Check detail endpoint shows REVIEWED status and the feedback
        var detailResponse = await leaderClient.GetAsync($"/api/v1/progress-reports/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<ProgressReportDetailDto>();
        Assert.NotNull(detail);
        Assert.Equal("REVIEWED", detail.Status);
        Assert.Single(detail.Feedbacks);

        // 8. Other supervisor attempts feedback on this project -> 403 Forbidden
        var otherFeedbackResponse = await otherSupervisorClient.PostAsJsonAsync(
            $"/api/v1/progress-reports/{created.Id}/feedback", new AddProgressReportFeedbackRequest("Unauthorized feedback"));
        Assert.Equal(HttpStatusCode.Forbidden, otherFeedbackResponse.StatusCode);
    }

    [Fact]
    public async Task Report_OutsideProject_UserCannotViewOrEdit_403()
    {
        var s = await SeedAsync();
        using var app = new Factory(database);
        using var leaderClient = app.CreateAuthenticatedClient(s.Accounts.Student, roles: [AppRoles.Student]);
        using var outsideClient = app.CreateAuthenticatedClient(s.Accounts.OutsideStaff, roles: [AppRoles.DepartmentStaff]);

        var createRequest = new CreateProgressReportRequest(
            "WEEKLY",
            DateOnly.FromDateTime(Now.AddDays(-7)),
            DateOnly.FromDateTime(Now),
            "Summary", null, null, null);

        var createResponse = await leaderClient.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/progress-reports", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<ProgressReportDto>();
        Assert.NotNull(created);

        // Outside user attempts to view
        var viewResponse = await outsideClient.GetAsync($"/api/v1/progress-reports/{created.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, viewResponse.StatusCode);

        // Outside user attempts to list
        var listResponse = await outsideClient.GetAsync($"/api/v1/projects/{s.ProjectId}/progress-reports");
        Assert.Equal(HttpStatusCode.Forbidden, listResponse.StatusCode);
    }

    [Fact]
    public async Task Report_ListReports_PaginationAndFilter()
    {
        var s = await SeedAsync();
        using var app = new Factory(database);
        using var leaderClient = app.CreateAuthenticatedClient(s.Accounts.Student, roles: [AppRoles.Student]);

        var req1 = new CreateProgressReportRequest("WEEKLY", DateOnly.FromDateTime(Now.AddDays(-14)), DateOnly.FromDateTime(Now.AddDays(-8)), "Week 1", null, null, null);
        var req2 = new CreateProgressReportRequest("WEEKLY", DateOnly.FromDateTime(Now.AddDays(-7)), DateOnly.FromDateTime(Now), "Week 2", null, null, null);

        await leaderClient.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/progress-reports", req1);
        await leaderClient.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/progress-reports", req2);

        var response = await leaderClient.GetAsync($"/api/v1/projects/{s.ProjectId}/progress-reports?reportType=WEEKLY&page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var paged = await response.Content.ReadFromJsonAsync<PagedResult<ProgressReportDto>>();
        Assert.NotNull(paged);
        Assert.Equal(2, paged.TotalCount);
        Assert.Equal(2, paged.Items.Count);
    }

    #region Finding 2: Authorization Integration Tests

    [Fact]
    public async Task Staff_CannotCreateOrUpdateProgressReportDraft_403()
    {
        var s = await SeedAsync();
        using var app = new Factory(database);
        using var leaderClient = app.CreateAuthenticatedClient(s.Accounts.Student, roles: [AppRoles.Student]);
        using var staffClient = app.CreateAuthenticatedClient(s.Accounts.Staff, roles: [AppRoles.DepartmentStaff]);

        // Staff attempts create draft -> 403
        var createRequest = new CreateProgressReportRequest("WEEKLY", DateOnly.FromDateTime(Now.AddDays(-7)), DateOnly.FromDateTime(Now), "Staff Draft", null, null, null);
        var createResponse = await staffClient.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/progress-reports", createRequest);
        Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);

        // Leader creates draft
        var leaderCreate = await leaderClient.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/progress-reports", createRequest);
        Assert.Equal(HttpStatusCode.Created, leaderCreate.StatusCode);
        var created = await leaderCreate.Content.ReadFromJsonAsync<ProgressReportDto>();

        // Staff attempts update draft -> 403
        var updateRequest = new UpdateProgressReportRequest("Staff Updated Summary", null, null, null);
        var updateResponse = await staffClient.PutAsJsonAsync($"/api/v1/progress-reports/{created!.Id}", updateRequest);
        Assert.Equal(HttpStatusCode.Forbidden, updateResponse.StatusCode);
    }

    [Fact]
    public async Task Supervisor_CannotCreateOrUpdateProgressReportDraft_403()
    {
        var s = await SeedAsync();
        using var app = new Factory(database);
        using var leaderClient = app.CreateAuthenticatedClient(s.Accounts.Student, roles: [AppRoles.Student]);
        using var supervisorClient = app.CreateAuthenticatedClient(s.Accounts.Lecturer, roles: [AppRoles.Lecturer]);

        // Supervisor attempts create draft -> 403
        var createRequest = new CreateProgressReportRequest("WEEKLY", DateOnly.FromDateTime(Now.AddDays(-7)), DateOnly.FromDateTime(Now), "Supervisor Draft", null, null, null);
        var createResponse = await supervisorClient.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/progress-reports", createRequest);
        Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);

        // Leader creates draft
        var leaderCreate = await leaderClient.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/progress-reports", createRequest);
        Assert.Equal(HttpStatusCode.Created, leaderCreate.StatusCode);
        var created = await leaderCreate.Content.ReadFromJsonAsync<ProgressReportDto>();

        // Supervisor attempts update draft -> 403
        var updateRequest = new UpdateProgressReportRequest("Supervisor Updated Summary", null, null, null);
        var updateResponse = await supervisorClient.PutAsJsonAsync($"/api/v1/progress-reports/{created!.Id}", updateRequest);
        Assert.Equal(HttpStatusCode.Forbidden, updateResponse.StatusCode);
    }

    [Fact]
    public async Task FormerMember_CannotCreateOrUpdateDraft_403()
    {
        var s = await SeedAsync();
        using var app = new Factory(database);
        using var leaderClient = app.CreateAuthenticatedClient(s.Accounts.Student, roles: [AppRoles.Student]);

        long formerStudentId;
        await using (var db = database.CreateContext())
        {
            var studentRole = await db.Roles.SingleAsync(r => r.Code == AppRoles.Student);
            var formerUser = new M.User
            {
                DepartmentId = s.Accounts.DepartmentId,
                Email = $"{Guid.NewGuid():N}@test.local",
                FullName = "Former Member",
                PasswordHash = "unused",
                Status = "ACTIVE",
                UserRoleUsers = [new() { RoleId = studentRole.Id }]
            };
            db.Users.Add(formerUser);
            await db.SaveChangesAsync();
            formerStudentId = formerUser.Id;

            var team = await db.Teams.FindAsync(s.TeamId);
            var tm = new M.TeamMember
            {
                TeamId = s.TeamId,
                AcademicSemesterId = team!.AcademicSemesterId,
                UserId = formerUser.Id,
                IsLeader = false,
                JoinedAt = Now.AddDays(-30),
                LeftAt = Now.AddDays(-5),
                CreatedAt = Now.AddDays(-30),
                UpdatedAt = Now.AddDays(-5)
            };
            db.TeamMembers.Add(tm);
            await db.SaveChangesAsync();
        }

        using var formerClient = app.CreateAuthenticatedClient(formerStudentId, roles: [AppRoles.Student]);

        // Former member attempts create draft -> 403
        var createRequest = new CreateProgressReportRequest("WEEKLY", DateOnly.FromDateTime(Now.AddDays(-7)), DateOnly.FromDateTime(Now), "Former Draft", null, null, null);
        var createResponse = await formerClient.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/progress-reports", createRequest);
        Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);

        // Leader creates draft
        var leaderCreate = await leaderClient.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/progress-reports", createRequest);
        var created = await leaderCreate.Content.ReadFromJsonAsync<ProgressReportDto>();

        // Former member attempts update draft -> 403
        var updateResponse = await formerClient.PutAsJsonAsync($"/api/v1/progress-reports/{created!.Id}", new UpdateProgressReportRequest("Hacked", null, null, null));
        Assert.Equal(HttpStatusCode.Forbidden, updateResponse.StatusCode);
    }

    #endregion

    #region Finding 5: Completeness Integration Tests

    [Fact]
    public async Task IncompleteDraft_CanBeCreatedAndUpdated()
    {
        var s = await SeedAsync();
        using var app = new Factory(database);
        using var memberClient = app.CreateAuthenticatedClient(s.MemberId, roles: [AppRoles.Student]);

        // Member creates draft with only Summary (partial content)
        var createRequest = new CreateProgressReportRequest(
            "WEEKLY", DateOnly.FromDateTime(Now.AddDays(-7)), DateOnly.FromDateTime(Now), "Partial Draft", null, null, null);
        var createResponse = await memberClient.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/progress-reports", createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<ProgressReportDto>();
        Assert.NotNull(created);
        Assert.Equal("DRAFT", created.Status);
        Assert.Null(created.CompletedWork);

        // Member updates draft with only Summary
        var updateResponse = await memberClient.PutAsJsonAsync(
            $"/api/v1/progress-reports/{created.Id}", new UpdateProgressReportRequest("Updated Partial", null, null, null));
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
    }

    [Fact]
    public async Task Submit_IncompleteDraft_Returns400BadRequest()
    {
        var s = await SeedAsync();
        using var app = new Factory(database);
        using var leaderClient = app.CreateAuthenticatedClient(s.Accounts.Student, roles: [AppRoles.Student]);

        // Create draft with only Summary
        var createRequest = new CreateProgressReportRequest(
            "WEEKLY", DateOnly.FromDateTime(Now.AddDays(-7)), DateOnly.FromDateTime(Now), "Incomplete Draft", null, null, null);
        var createResponse = await leaderClient.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/progress-reports", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<ProgressReportDto>();

        // Attempt submit incomplete draft -> 400 Bad Request
        var submitResponse = await leaderClient.PostAsync($"/api/v1/progress-reports/{created!.Id}/submit", null);
        Assert.Equal(HttpStatusCode.BadRequest, submitResponse.StatusCode);

        // Update with whitespace completedWork -> still 400
        await leaderClient.PutAsJsonAsync(
            $"/api/v1/progress-reports/{created.Id}",
            new UpdateProgressReportRequest("Incomplete Draft", "   ", "Plan A", "Risk A"));
        var submitResponse2 = await leaderClient.PostAsync($"/api/v1/progress-reports/{created.Id}/submit", null);
        Assert.Equal(HttpStatusCode.BadRequest, submitResponse2.StatusCode);

        // Fill all required fields -> submit succeeds with 200 OK
        await leaderClient.PutAsJsonAsync(
            $"/api/v1/progress-reports/{created.Id}",
            new UpdateProgressReportRequest("Complete Draft", "Done A", "Plan A", "Risk A"));
        var submitSuccess = await leaderClient.PostAsync($"/api/v1/progress-reports/{created.Id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitSuccess.StatusCode);
        var submitted = await submitSuccess.Content.ReadFromJsonAsync<ProgressReportDto>();
        Assert.Equal("SUBMITTED", submitted!.Status);
    }

    #endregion
}
