using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Academic.Abstractions;
using AIPMS.Application.Features.Academic.Models;
using AIPMS.Application.Abstractions.Auditing;
using Xunit;

namespace AIPMS.IntegrationTests;

public sealed class ProjectEndpointTests : IClassFixture<ProjectEndpointTests.ProjectWebApplicationFactory>
{
    public class ProjectWebApplicationFactory : AipmsWebApplicationFactory
    {
        public TestProjectRepository ProjectRepository { get; } = new();
        public TestAcademicRepository AcademicRepository { get; } = new();

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IProjectRepository>();
                services.AddSingleton<IProjectRepository>(ProjectRepository);

                services.RemoveAll<IAcademicStructureRepository>();
                services.AddSingleton<IAcademicStructureRepository>(AcademicRepository);

                services.RemoveAll<IAuditTrail>();
                services.AddSingleton<IAuditTrail, NoOpAuditTrail>();
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
        // Reset repositories
        _factory.ProjectRepository.Projects.Clear();
        _factory.ProjectRepository.StatusHistories.Clear();
        _factory.ProjectRepository.ProjectDeptIds.Clear();
        _factory.ProjectRepository.IsLeader = true;
        _factory.ProjectRepository.IsTeamEligible = true;
        _factory.ProjectRepository.IsRegistrationOpen = true;
        _factory.ProjectRepository.UserActiveTeamId = 1;
        _factory.ProjectRepository.MajorsExist = true;

        long majorId = 301;
        long leaderUserId = 1001;
        long memberUserId = 1002;
        long staffUserId = 1003;

        // Prepare scope for staff
        _factory.AcademicRepository.Scopes[staffUserId] = new AcademicUserScope(1, 100);
        _factory.ProjectRepository.ProjectDeptIds.Add(100);

        // Prepare clients
        var leaderClient = _factory.CreateAuthenticatedClient(leaderUserId, "leader@aipms.test", "Leader", AppRoles.Student);
        var memberClient = _factory.CreateAuthenticatedClient(memberUserId, "member@aipms.test", "Member", AppRoles.Student);
        var staffClient = _factory.CreateAuthenticatedClient(staffUserId, "staff@aipms.test", "Staff", AppRoles.DepartmentStaff);

        // 2. Create Project Draft (Student Leader)
        var createRequest = new CreateProjectDraftRequest(
            Title: "Proposal E2E",
            Description: "A great capstone project",
            Objectives: "Achieve all milestones",
            ProblemStatement: "Too many manual tasks",
            ExpectedOutput: "A fully working system",
            RequiredMajorIds: [majorId],
            Domain: "Software Engineering",
            Technologies: ["React", ".NET 8"],
            Keywords: ["Management", "Automation"]
        );

        var createResponse = await leaderClient.PostAsJsonAsync("api/v1/projects", createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var project = await createResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);
        Assert.Equal("DRAFT", project.Status);
        Assert.Equal("Proposal E2E", project.Title);

        // 3. Prevent duplicate active project per team (Rule Check)
        _factory.ProjectRepository.HasActiveProject = true; // Simulates active project exists
        var duplicateCreateResponse = await leaderClient.PostAsJsonAsync("api/v1/projects", createRequest);
        Assert.Equal(HttpStatusCode.Conflict, duplicateCreateResponse.StatusCode);
        _factory.ProjectRepository.HasActiveProject = false;

        // 4. Prevent non-leader from updating
        var updateRequest = new UpdateProjectDraftRequest(
            ConcurrencyToken: project.ConcurrencyToken,
            Title: "Proposal E2E Updated",
            Description: "A great capstone project",
            Objectives: "Achieve all milestones",
            ProblemStatement: "Too many manual tasks",
            ExpectedOutput: "A fully working system",
            RequiredMajorIds: [majorId],
            Domain: "Software Engineering",
            Technologies: ["React", ".NET 8"],
            Keywords: ["Management", "Automation"]
        );

        _factory.ProjectRepository.IsLeader = false;
        var updateNonLeaderResponse = await memberClient.PutAsJsonAsync($"api/v1/projects/{project.Id}", updateRequest);
        Assert.Equal(HttpStatusCode.Forbidden, updateNonLeaderResponse.StatusCode);
        _factory.ProjectRepository.IsLeader = true;

        // 5. Leader updates draft successfully
        var updateResponse = await leaderClient.PutAsJsonAsync($"api/v1/projects/{project.Id}", updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        project = await updateResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);
        Assert.Equal("Proposal E2E Updated", project.Title);

        // 6. Leader configures Majors
        var setMajorsRequest = new SetProjectMajorsRequest(project.ConcurrencyToken, [majorId]);
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
        Assert.Equal(HttpStatusCode.BadRequest, invalidRevisionResponse.StatusCode); // Fluent validation fails

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
        Assert.NotEmpty(history);
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

public sealed class NoOpAuditTrail : IAuditTrail
{
    public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

public sealed class TestAcademicRepository : IAcademicStructureRepository
{
    public Dictionary<long, AcademicUserScope> Scopes { get; } = new();

    public Task<AcademicUserScope?> GetUserScopeAsync(long userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Scopes.GetValueOrDefault(userId));

    public Task<AcademicOrganization> CreateOrganizationAsync(string code, string name, string? description, DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<AcademicDepartment> CreateDepartmentAsync(long organizationId, string code, string name, string? description, DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<AcademicMajor> CreateMajorAsync(long departmentId, string code, string name, string? description, DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    
    public Task<AcademicOrganization?> GetOrganizationAsync(long organizationId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<PagedResult<AcademicOrganization>> GetOrganizationsAsync(string? search, bool? isActive, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<AcademicDepartment?> GetDepartmentAsync(long departmentId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<PagedResult<AcademicDepartment>> GetDepartmentsAsync(long? organizationId, string? search, bool? isActive, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<AcademicMajor?> GetMajorAsync(long majorId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<PagedResult<AcademicMajor>> GetMajorsAsync(long? organizationId, long? departmentId, string? search, bool? isActive, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    
    public Task<bool> OrganizationCodeOrNameExistsAsync(string code, string name, long? excludedOrganizationId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<bool> DepartmentCodeOrNameExistsAsync(long organizationId, string code, string name, long? excludedDepartmentId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<bool> MajorCodeOrNameExistsAsync(long departmentId, string code, string name, long? excludedMajorId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    
    public Task<AcademicOrganization> UpdateOrganizationAsync(long organizationId, string code, string name, string? description, DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<AcademicOrganization> SetOrganizationActiveAsync(long organizationId, bool isActive, DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    
    public Task<AcademicDepartment> UpdateDepartmentAsync(long departmentId, string code, string name, string? description, DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<AcademicDepartment> SetDepartmentActiveAsync(long departmentId, bool isActive, DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    
    public Task<AcademicMajor> UpdateMajorAsync(long majorId, long departmentId, string code, string name, string? description, DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<AcademicMajor> SetMajorActiveAsync(long majorId, bool isActive, DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    
    public Task<IReadOnlyList<AcademicHierarchyOrganization>> GetHierarchyAsync(long? organizationId, string? search, bool includeInactive, CancellationToken cancellationToken = default) => throw new NotImplementedException();
}

public sealed class TestProjectRepository : IProjectRepository
{
    private long _nextProjectId = 100;
    private long _nextHistoryId = 200;

    public Dictionary<long, ProjectDto> Projects { get; } = new();
    public Dictionary<long, List<ProjectStatusHistoryDto>> StatusHistories { get; } = new();
    
    public long? UserActiveTeamId { get; set; }
    public bool IsLeader { get; set; }
    public bool HasActiveProject { get; set; }
    public bool IsTeamEligible { get; set; } = true;
    public bool IsRegistrationOpen { get; set; } = true;
    public bool MajorsExist { get; set; } = true;
    public List<long> ProjectDeptIds { get; } = new();
    public bool CanView { get; set; } = true;

    public Task<ProjectDto?> GetByIdAsync(long id, CancellationToken cancellationToken) =>
        Task.FromResult(Projects.GetValueOrDefault(id));

    public Task<PagedResult<ProjectSummaryDto>> GetProjectsAsync(
        string? status,
        long? teamId,
        long? semesterId,
        long? majorId,
        string? tag,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var list = Projects.Values
            .Select(p => new ProjectSummaryDto(p.Id, p.TeamId, p.TeamName, p.Code, p.Title, p.Status, p.CreatedAt, p.SubmittedAt, p.Majors, p.Tags))
            .ToArray();
        return Task.FromResult(new PagedResult<ProjectSummaryDto>(list, page, pageSize, list.Length));
    }

    public Task<PagedResult<ProjectSummaryDto>> GetReviewQueueAsync(
        long? departmentId,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var list = Projects.Values
            .Select(p => new ProjectSummaryDto(p.Id, p.TeamId, p.TeamName, p.Code, p.Title, p.Status, p.CreatedAt, p.SubmittedAt, p.Majors, p.Tags))
            .ToArray();
        return Task.FromResult(new PagedResult<ProjectSummaryDto>(list, page, pageSize, list.Length));
    }

    public Task<bool> HasActiveProjectAsync(long teamId, CancellationToken cancellationToken) =>
        Task.FromResult(HasActiveProject);

    public Task<long?> GetActiveRegistrationSemesterIdAsync(long userId, DateTime currentUtc, CancellationToken cancellationToken) =>
        Task.FromResult(IsRegistrationOpen ? (long?)1 : null);

    public Task<long?> GetUserActiveTeamIdAsync(long userId, long semesterId, CancellationToken cancellationToken) =>
        Task.FromResult(UserActiveTeamId);

    public Task<bool> IsTeamLeaderAsync(long teamId, long userId, CancellationToken cancellationToken) =>
        Task.FromResult(IsLeader);

    public Task<ProjectDto> CreateDraftAsync(
        long teamId,
        long userId,
        string title,
        string? description,
        string? objectives,
        string? problemStatement,
        string? expectedOutput,
        IReadOnlyList<long> majorIds,
        string domain,
        IReadOnlyList<string> technologies,
        IReadOnlyList<string> keywords,
        CancellationToken cancellationToken)
    {
        var id = _nextProjectId++;
        var project = new ProjectDto(
            id,
            teamId,
            "Team " + teamId,
            "PRJ" + id,
            title,
            description,
            objectives,
            "DRAFT",
            DateTime.UtcNow,
            null,
            null,
            null,
            userId,
            "User " + userId,
            DateTime.UtcNow,
            DateTime.UtcNow,
            problemStatement,
            expectedOutput,
            Convert.ToBase64String(BitConverter.GetBytes((long)id)),
            majorIds.Select(m => new ProjectMajorDto(m, m, "M" + m, "Major " + m)).ToArray(),
            new List<ProjectTagDto> { new(1, domain, "DOMAIN") }
                .Concat(technologies.Select(t => new ProjectTagDto(2, t, "TECHNOLOGY")))
                .Concat(keywords.Select(k => new ProjectTagDto(3, k, "KEYWORD")))
                .ToArray()
        );
        Projects[id] = project;
        return Task.FromResult(project);
    }

    public Task<ProjectDto> UpdateDraftAsync(
        long projectId,
        string concurrencyToken,
        string title,
        string? description,
        string? objectives,
        string? problemStatement,
        string? expectedOutput,
        IReadOnlyList<long> majorIds,
        string domain,
        IReadOnlyList<string> technologies,
        IReadOnlyList<string> keywords,
        CancellationToken cancellationToken)
    {
        var existing = Projects[projectId];
        if (existing.ConcurrencyToken != concurrencyToken)
        {
            throw new AIPMS.Application.Common.Exceptions.ConflictException("Concurrency token mismatch.");
        }
        var updated = existing with
        {
            Title = title,
            Description = description,
            Objectives = objectives,
            ProblemStatement = problemStatement,
            ExpectedOutput = expectedOutput,
            ConcurrencyToken = Convert.ToBase64String(BitConverter.GetBytes((long)(projectId + 1))),
            Majors = majorIds.Select(m => new ProjectMajorDto(m, m, "M" + m, "Major " + m)).ToArray(),
            Tags = new List<ProjectTagDto> { new(1, domain, "DOMAIN") }
                .Concat(technologies.Select(t => new ProjectTagDto(2, t, "TECHNOLOGY")))
                .Concat(keywords.Select(k => new ProjectTagDto(3, k, "KEYWORD")))
                .ToArray()
        };
        Projects[projectId] = updated;
        return Task.FromResult(updated);
    }

    public Task<ProjectDto> UpdateStatusAsync(
        long projectId,
        string concurrencyToken,
        string oldStatus,
        string newStatus,
        long actorUserId,
        string? reason,
        CancellationToken cancellationToken)
    {
        var existing = Projects[projectId];
        if (existing.ConcurrencyToken != concurrencyToken)
        {
            throw new AIPMS.Application.Common.Exceptions.ConflictException("Concurrency token mismatch.");
        }

        var updated = existing with
        {
            Status = newStatus,
            SubmittedAt = newStatus == "SUBMITTED" ? DateTime.UtcNow : existing.SubmittedAt,
            ApprovedAt = newStatus == "APPROVED" ? DateTime.UtcNow : existing.ApprovedAt,
            ConcurrencyToken = Convert.ToBase64String(BitConverter.GetBytes((long)(projectId + 2)))
        };
        Projects[projectId] = updated;

        if (!StatusHistories.ContainsKey(projectId))
        {
            StatusHistories[projectId] = [];
        }
        StatusHistories[projectId].Add(new ProjectStatusHistoryDto(
            _nextHistoryId++,
            projectId,
            oldStatus,
            newStatus,
            actorUserId,
            "Actor " + actorUserId,
            reason,
            DateTime.UtcNow
        ));

        return Task.FromResult(updated);
    }

    public Task<IReadOnlyList<ProjectStatusHistoryDto>> GetStatusHistoryAsync(
        long projectId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ProjectStatusHistoryDto> list = StatusHistories.GetValueOrDefault(projectId) ?? [];
        return Task.FromResult(list);
    }

    public Task<bool> IsSemesterRegistrationOpenAsync(
        long semesterId,
        DateTime currentUtc,
        CancellationToken cancellationToken) =>
        Task.FromResult(IsRegistrationOpen);

    public Task<long?> GetSemesterIdByTeamIdAsync(
        long teamId,
        CancellationToken cancellationToken) =>
        Task.FromResult((long?)1);

    public Task<bool> ValidateMajorsExistAsync(
        IEnumerable<long> majorIds,
        CancellationToken cancellationToken) =>
        Task.FromResult(MajorsExist);

    public Task<bool> IsTeamEligibleAsync(
        long teamId,
        CancellationToken cancellationToken) =>
        Task.FromResult(IsTeamEligible);

    public Task<bool> ProjectBelongsToTeamAsync(
        long projectId,
        long teamId,
        CancellationToken cancellationToken) =>
        Task.FromResult(Projects.ContainsKey(projectId) && Projects[projectId].TeamId == teamId);

    public Task<IReadOnlyList<long>> GetProjectMajorDepartmentIdsAsync(
        long projectId,
        CancellationToken cancellationToken) =>
        Task.FromResult((IReadOnlyList<long>)ProjectDeptIds);

    public Task<bool> CanUserViewProjectAsync(
        long projectId,
        long userId,
        bool isAdmin,
        long? staffScopeDepartmentId,
        CancellationToken cancellationToken) =>
        Task.FromResult(CanView);

    public Task<ProjectProgressSummaryDto> GetProjectProgressSummaryAsync(
        long projectId,
        CancellationToken cancellationToken) =>
        Task.FromResult(new ProjectProgressSummaryDto(projectId, 0, 0, 0, 0, 0, 0, 0.0));

    public Task<ProjectTimelineDataDto> GetTimelineDataAsync(
        long projectId,
        CancellationToken cancellationToken) =>
        Task.FromResult(new ProjectTimelineDataDto(projectId, Array.Empty<TimelineMilestoneDto>()));
}
