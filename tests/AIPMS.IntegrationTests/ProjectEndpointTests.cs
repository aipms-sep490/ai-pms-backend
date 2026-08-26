using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;
using Microsoft.Extensions.Configuration;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AIPMS.IntegrationTests;

public sealed class ProjectEndpointTests : IClassFixture<ProjectEndpointTests.ProjectWebApplicationFactory>
{
    public class ProjectWebApplicationFactory : AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var currentConfig = configuration.Build();
                var connString = currentConfig.GetConnectionString("DefaultConnection");
                if (string.IsNullOrWhiteSpace(connString) || connString.Contains("(local)"))
                {
                    // Fallback default for local test run
                    connString = "Server=(local);Database=AIPMS_Tests;Trusted_Connection=True;TrustServerCertificate=True;";
                }

                try
                {
                    var sqlBuilder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connString)
                    {
                        InitialCatalog = "AIPMS_Tests",
                        TrustServerCertificate = true,
                        Encrypt = false
                    };
                    connString = sqlBuilder.ConnectionString;
                }
                catch
                {
                    // Fallback in case of parsing error
                    connString = "Server=(local);Database=AIPMS_Tests;Trusted_Connection=True;TrustServerCertificate=True;";
                }

                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = connString
                });
            });
        }
    }

    private readonly ProjectWebApplicationFactory _factory;

    public ProjectEndpointTests(ProjectWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Project_EndToEndLifecycle_Succeeds()
    {
        // 1. Arrange & Seed baseline database data
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AipmsDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..6];
        var org = new Organization { Code = "ORG" + suffix, Name = "Org " + suffix, IsActive = true };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();

        var dept = new Department { OrganizationId = org.Id, Code = "IT" + suffix, Name = "IT " + suffix, IsActive = true };
        context.Departments.Add(dept);
        await context.SaveChangesAsync();

        var major = new Major { DepartmentId = dept.Id, Code = "SE" + suffix, Name = "SE " + suffix, IsActive = true };
        context.Majors.Add(major);
        await context.SaveChangesAsync();

        var semester = new AcademicSemester
        {
            OrganizationId = org.Id,
            Code = "FA" + suffix,
            Name = "Fall " + suffix,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(90)),
            Status = "ACTIVE"
        };
        context.AcademicSemesters.Add(semester);
        await context.SaveChangesAsync();

        var period = new ProjectPeriod
        {
            AcademicSemesterId = semester.Id,
            Code = "REG" + suffix,
            Name = "Registration " + suffix,
            PeriodType = "REGISTRATION",
            StartAt = DateTime.UtcNow.AddDays(-5),
            EndAt = DateTime.UtcNow.AddDays(5),
            Status = "ACTIVE"
        };
        context.ProjectPeriods.Add(period);
        await context.SaveChangesAsync();

        // Create leader and member users
        var leaderUser = new User
        {
            MajorId = major.Id,
            Email = $"leader{suffix}@aipms.test",
            PasswordHash = "HASH",
            FullName = "Leader " + suffix,
            Status = "ACTIVE"
        };
        context.Users.Add(leaderUser);

        var memberUser = new User
        {
            MajorId = major.Id,
            Email = $"member{suffix}@aipms.test",
            PasswordHash = "HASH",
            FullName = "Member " + suffix,
            Status = "ACTIVE"
        };
        context.Users.Add(memberUser);

        var staffUser = new User
        {
            DepartmentId = dept.Id,
            Email = $"staff{suffix}@aipms.test",
            PasswordHash = "HASH",
            FullName = "Staff " + suffix,
            Status = "ACTIVE"
        };
        context.Users.Add(staffUser);
        await context.SaveChangesAsync();

        // Create team and team members (Leader is Team Leader)
        var team = new Team
        {
            AcademicSemesterId = semester.Id,
            Code = "T" + suffix,
            Name = "Team " + suffix,
            Status = "ELIGIBLE",
            CreatedBy = leaderUser.Id
        };
        context.Teams.Add(team);
        await context.SaveChangesAsync();

        context.TeamMembers.Add(new TeamMember
        {
            TeamId = team.Id,
            AcademicSemesterId = semester.Id,
            UserId = leaderUser.Id,
            IsLeader = true
        });

        context.TeamMembers.Add(new TeamMember
        {
            TeamId = team.Id,
            AcademicSemesterId = semester.Id,
            UserId = memberUser.Id,
            IsLeader = false
        });
        await context.SaveChangesAsync();

        // Prepare clients
        var leaderClient = _factory.CreateAuthenticatedClient(leaderUser.Id, leaderUser.Email, leaderUser.FullName, AppRoles.Student);
        var memberClient = _factory.CreateAuthenticatedClient(memberUser.Id, memberUser.Email, memberUser.FullName, AppRoles.Student);
        var staffClient = _factory.CreateAuthenticatedClient(staffUser.Id, staffUser.Email, staffUser.FullName, AppRoles.DepartmentStaff);

        // 2. Create Project Draft (Student Leader)
        var createRequest = new CreateProjectDraftRequest(
            Title: "Proposal " + suffix,
            Description: "A great capstone project",
            Objectives: "Achieve all milestones",
            ProblemStatement: "Too many manual tasks",
            ExpectedOutput: "A fully working system",
            RequiredMajorIds: [major.Id],
            Domain: "Software Engineering",
            Technologies: ["React", ".NET 8"],
            Keywords: ["Management", "Automation"]
        );

        var createResponse = await leaderClient.PostAsJsonAsync("api/v1/projects", createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var project = await createResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);
        Assert.Equal("DRAFT", project.Status);
        Assert.Equal("Proposal " + suffix, project.Title);

        // 3. Prevent duplicate active project per team (Rule Check)
        var duplicateCreateResponse = await leaderClient.PostAsJsonAsync("api/v1/projects", createRequest);
        Assert.Equal(HttpStatusCode.Conflict, duplicateCreateResponse.StatusCode);

        // 4. Prevent non-leader from updating
        var updateRequest = new UpdateProjectDraftRequest(
            ConcurrencyToken: project.ConcurrencyToken,
            Title: "Proposal " + suffix + " Updated",
            Description: "A great capstone project",
            Objectives: "Achieve all milestones",
            ProblemStatement: "Too many manual tasks",
            ExpectedOutput: "A fully working system",
            RequiredMajorIds: [major.Id],
            Domain: "Software Engineering",
            Technologies: ["React", ".NET 8"],
            Keywords: ["Management", "Automation"]
        );

        var updateNonLeaderResponse = await memberClient.PutAsJsonAsync($"api/v1/projects/{project.Id}", updateRequest);
        Assert.Equal(HttpStatusCode.Forbidden, updateNonLeaderResponse.StatusCode);

        // 5. Leader updates draft successfully
        var updateResponse = await leaderClient.PutAsJsonAsync($"api/v1/projects/{project.Id}", updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        project = await updateResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);
        Assert.Equal("Proposal " + suffix + " Updated", project.Title);

        // 6. Leader configures Majors
        var setMajorsRequest = new SetProjectMajorsRequest(project.ConcurrencyToken, [major.Id]);
        var setMajorsResponse = await leaderClient.PutAsJsonAsync($"api/v1/projects/{project.Id}/majors", setMajorsRequest);
        Assert.Equal(HttpStatusCode.OK, setMajorsResponse.StatusCode);

        project = await setMajorsResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);

        // 7. Submit Project (Leader)
        var submitRequest = new SubmitProjectRequest(project.ConcurrencyToken);
        var submitResponse = await leaderClient.PostAsJsonAsync($"api/v1/projects/{project.Id}/submit", submitRequest);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        project = await submitResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);
        Assert.Equal("SUBMITTED", project.Status);

        // 8. Start Review (Staff)
        var startReviewRequest = new SubmitProjectRequest(project.ConcurrencyToken);
        var startReviewResponse = await staffClient.PostAsJsonAsync($"api/v1/projects/{project.Id}/start-review", startReviewRequest);
        Assert.Equal(HttpStatusCode.OK, startReviewResponse.StatusCode);

        project = await startReviewResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);
        Assert.Equal("UNDER_REVIEW", project.Status);

        // 9. Request Revision (Staff) - Rejection/Revision reason is mandatory
        var invalidRevisionRequest = new ProjectReviewRequest(project.ConcurrencyToken, "   ");
        var invalidRevisionResponse = await staffClient.PostAsJsonAsync($"api/v1/projects/{project.Id}/revision", invalidRevisionRequest);
        Assert.Equal(HttpStatusCode.BadRequest, invalidRevisionResponse.StatusCode); // Validation failed

        var revisionRequest = new ProjectReviewRequest(project.ConcurrencyToken, "Please clarify database design.");
        var revisionResponse = await staffClient.PostAsJsonAsync($"api/v1/projects/{project.Id}/revision", revisionRequest);
        Assert.Equal(HttpStatusCode.OK, revisionResponse.StatusCode);

        project = await revisionResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);
        Assert.Equal("REVISION_REQUIRED", project.Status);

        // 10. Resubmit Project (Leader)
        var resubmitRequest = new SubmitProjectRequest(project.ConcurrencyToken);
        var resubmitResponse = await leaderClient.PostAsJsonAsync($"api/v1/projects/{project.Id}/resubmit", resubmitRequest);
        Assert.Equal(HttpStatusCode.OK, resubmitResponse.StatusCode);

        project = await resubmitResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);
        Assert.Equal("SUBMITTED", project.Status);

        // 11. Start Review again (Staff)
        startReviewResponse = await staffClient.PostAsJsonAsync($"api/v1/projects/{project.Id}/start-review", startReviewRequest with { ConcurrencyToken = project.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.OK, startReviewResponse.StatusCode);

        project = await startReviewResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);

        // 12. Approve Project (Staff)
        var approveRequest = new SubmitProjectRequest(project.ConcurrencyToken);
        var approveResponse = await staffClient.PostAsJsonAsync($"api/v1/projects/{project.Id}/approve", approveRequest);
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        project = await approveResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);
        Assert.Equal("APPROVED", project.Status);

        // 13. Verify Status History
        var historyResponse = await leaderClient.GetAsync($"api/v1/projects/{project.Id}/history");
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);

        var history = await historyResponse.Content.ReadFromJsonAsync<IReadOnlyList<ProjectStatusHistoryDto>>();
        Assert.NotNull(history);
        Assert.True(history.Count >= 5); // DRAFT->SUBMITTED->UNDER_REVIEW->REVISION_REQUIRED->SUBMITTED->UNDER_REVIEW->APPROVED
    }

    [Fact]
    public async Task GetProjects_ReturnsPaginatedList()
    {
        // Arrange
        var client = _factory.CreateAuthenticatedClient(roles: [AppRoles.Student]);

        // Act
        var response = await client.GetAsync("api/v1/projects?page=1&pageSize=5");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProjectSummaryDto>>();
        Assert.NotNull(result);
        Assert.True(result.PageSize == 5);
    }
}
