using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Academic.Abstractions;
using AIPMS.Application.Features.Academic.Models;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Teams.Abstractions;
using AIPMS.Application.Features.Notifications.Abstractions;
using AIPMS.Application.Features.Notifications.Events;
using AIPMS.Application.Features.Topics.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.TestHost;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests;

public sealed class ProjectEndpointTests : IClassFixture<ProjectEndpointTests.ProjectWebApplicationFactory>
{
    public class ProjectWebApplicationFactory : AipmsWebApplicationFactory
    {
        public TestProjectRepository ProjectRepository { get; } = new();
        public TestAcademicRepository AcademicRepository { get; } = new();
        public RecordingNotifications Notifications { get; } = new();
        public StubTopicSelectionGuard TopicSelectionGuard { get; } = new();

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IProjectRepository>();
                services.AddSingleton<IProjectRepository>(ProjectRepository);
                services.RemoveAll<ITeamRegistrationGuard>();
                services.AddSingleton<ITeamRegistrationGuard>(new StubRegistrationGuard(ProjectRepository));

                services.RemoveAll<IAcademicStructureRepository>();
                services.AddSingleton<IAcademicStructureRepository>(AcademicRepository);

                services.RemoveAll<ITopicSelectionGuard>();
                services.AddSingleton<ITopicSelectionGuard>(TopicSelectionGuard);

                services.RemoveAll<IAuditTrail>();
                services.AddSingleton<IAuditTrail, NoOpAuditTrail>();
                services.RemoveAll<IWorkflowNotificationWriter>();
                services.AddSingleton<IWorkflowNotificationWriter>(Notifications);
            });
        }
    }

    private readonly ProjectWebApplicationFactory _factory;

    public sealed class RecordingNotifications : IWorkflowNotificationWriter
    {
        public List<WorkflowNotificationEvent> Events { get; } = [];
        public Task WriteAsync(WorkflowNotificationEvent notification, CancellationToken ct)
        {
            Events.Add(notification);
            return Task.CompletedTask;
        }
    }

    // This suite tests the HTTP/project contracts using stubs; SQL-backed team
    // registration and transaction behavior is exercised in Teams integration tests.
    private sealed class StubRegistrationGuard(TestProjectRepository repository) : ITeamRegistrationGuard
    {
        public Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct) => action(ct);
        public Task ValidateAsync(long teamId, CancellationToken ct) =>
            repository.IsTeamEligible ? Task.CompletedTask : throw new ConflictException("Team is not eligible.");
    }

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
        _factory.Notifications.Events.Clear();
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
        var revisionNotice = Assert.Single(_factory.Notifications.Events.Where(e => e.SourceId == project.Id));
        Assert.Equal(WorkflowNotificationKind.ProjectRevisionRequested, revisionNotice.Kind);
        Assert.Equal(staffUserId, revisionNotice.ActorId);
        Assert.Equal(project.ConcurrencyToken, revisionNotice.SourceVersion);

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
        var projectEvents = _factory.Notifications.Events.Where(e => e.SourceId == project.Id).ToArray();
        Assert.Equal(2, projectEvents.Length);
        var notification = Assert.Single(projectEvents.Where(e => e.Kind == WorkflowNotificationKind.ProjectApproved));
        Assert.Equal(staffUserId, notification.ActorId);
        Assert.Equal(project.UpdatedAt, notification.OccurredAt);

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

    [Fact]
    public async Task Archive_requires_completed_project_staff_scope_and_current_token()
    {
        _factory.ProjectRepository.Projects.Clear();
        _factory.ProjectRepository.StatusHistories.Clear();
        _factory.ProjectRepository.ProjectDeptIds.Clear();
        _factory.ProjectRepository.ProjectDeptIds.Add(100);
        const long projectId = 77;
        const long staffId = 1003;
        var project = new ProjectDto(projectId, 1, "Team", "PRJ-77", "Finished", null, null, "COMPLETED",
            DateTime.UtcNow.AddDays(-1), null, null, DateTime.UtcNow.AddHours(-1), 1001, "Student",
            DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddHours(-1), null, null, "dG9rZW4=", [], []);
        _factory.ProjectRepository.Projects[projectId] = project;
        _factory.AcademicRepository.Scopes[staffId] = new AcademicUserScope(1, 100);
        using var staff = _factory.CreateAuthenticatedClient(staffId, roles: [AppRoles.DepartmentStaff]);
        var response = await staff.PostAsJsonAsync($"api/v1/projects/{projectId}/archive",
            new ArchiveProjectRequest(project.ConcurrencyToken, "Lifecycle closed"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var archived = await response.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.Equal("ARCHIVED", archived!.Status);
        Assert.Equal("ARCHIVED", _factory.ProjectRepository.Projects[projectId].Status);
        Assert.Equal("Lifecycle closed", _factory.ProjectRepository.StatusHistories[projectId].Single().Reason);

        using var student = _factory.CreateAuthenticatedClient(1001, roles: [AppRoles.Student]);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.PostAsJsonAsync($"api/v1/projects/{projectId}/archive",
            new ArchiveProjectRequest(archived.ConcurrencyToken, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync($"api/v1/projects/{projectId}/archive",
            new ArchiveProjectRequest(project.ConcurrencyToken, null))).StatusCode);
    }

    [Fact]
    public async Task Archive_rejects_non_completed_project()
    {
        _factory.ProjectRepository.Projects.Clear();
        _factory.ProjectRepository.ProjectDeptIds.Clear();
        _factory.ProjectRepository.ProjectDeptIds.Add(100);
        var project = new ProjectDto(78, 1, "Team", "PRJ-78", "In progress", null, null, "ACTIVE",
            DateTime.UtcNow, null, null, null, 1001, "Student", DateTime.UtcNow, DateTime.UtcNow,
            null, null, "dG9rZW4=", [], []);
        _factory.ProjectRepository.Projects[project.Id] = project;
        _factory.AcademicRepository.Scopes[1003] = new AcademicUserScope(1, 100);
        using var staff = _factory.CreateAuthenticatedClient(1003, roles: [AppRoles.DepartmentStaff]);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync($"api/v1/projects/{project.Id}/archive",
            new ArchiveProjectRequest(project.ConcurrencyToken, null))).StatusCode);
    }

    private void ResetProjectRepositoryState()
    {
        _factory.ProjectRepository.Projects.Clear();
        _factory.ProjectRepository.StatusHistories.Clear();
        _factory.ProjectRepository.ProjectDeptIds.Clear();
        _factory.ProjectRepository.IsLeader = true;
        _factory.ProjectRepository.IsTeamEligible = true;
        _factory.ProjectRepository.IsRegistrationOpen = true;
        _factory.ProjectRepository.UserActiveTeamId = 1;
        _factory.ProjectRepository.MajorsExist = true;
        _factory.ProjectRepository.HasActiveProject = false;
        _factory.TopicSelectionGuard.OnValidate = null;
    }

    [Fact]
    public async Task SelectPublishedTopic_PersistsTopicId()
    {
        ResetProjectRepositoryState();

        var leaderClient = _factory.CreateAuthenticatedClient(1001, roles: [AppRoles.Student]);
        var createResponse = await leaderClient.PostAsJsonAsync("api/v1/projects", new CreateProjectDraftRequest(
            "Proposal", "Desc", "Objs", "Problem", "Output", [100], "Domain", ["Tech"], ["Kw"]));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var project = await createResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);

        var selectResponse = await leaderClient.PutAsJsonAsync(
            $"api/v1/projects/{project.Id}/topic",
            new SelectProjectTopicRequest(50, project.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.OK, selectResponse.StatusCode);

        var updated = await selectResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(updated);
        Assert.Equal(50, updated.TopicId);
        Assert.Equal("PUBLISHED_TOPIC", updated.ProposalSource);
        Assert.NotNull(updated.SelectedTopic);
        Assert.Equal(50, updated.SelectedTopic.Id);
    }

    [Fact]
    public async Task SelectPublishedTopic_SetsProposalSourcePublishedTopic()
    {
        ResetProjectRepositoryState();

        var leaderClient = _factory.CreateAuthenticatedClient(1001, roles: [AppRoles.Student]);
        var createResponse = await leaderClient.PostAsJsonAsync("api/v1/projects", new CreateProjectDraftRequest(
            "Proposal", "Desc", "Objs", "Problem", "Output", [100], "Domain", ["Tech"], ["Kw"]));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var project = await createResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);
        Assert.Equal("STUDENT_PROPOSAL", project.ProposalSource);

        var selectResponse = await leaderClient.PutAsJsonAsync(
            $"api/v1/projects/{project.Id}/topic",
            new SelectProjectTopicRequest(77, project.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.OK, selectResponse.StatusCode);

        var updated = await selectResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(updated);
        Assert.Equal("PUBLISHED_TOPIC", updated.ProposalSource);
    }

    [Fact]
    public async Task GetProject_ReturnsSelectedTopic()
    {
        ResetProjectRepositoryState();

        var leaderClient = _factory.CreateAuthenticatedClient(1001, roles: [AppRoles.Student]);
        var createResponse = await leaderClient.PostAsJsonAsync("api/v1/projects", new CreateProjectDraftRequest(
            "Proposal", "Desc", "Objs", "Problem", "Output", [100], "Domain", ["Tech"], ["Kw"]));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var project = await createResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);

        await leaderClient.PutAsJsonAsync(
            $"api/v1/projects/{project.Id}/topic",
            new SelectProjectTopicRequest(88, project.ConcurrencyToken));

        var getResponse = await leaderClient.GetAsync($"api/v1/projects/{project.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var fetched = await getResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(fetched);
        Assert.Equal(88, fetched.TopicId);
        Assert.Equal("PUBLISHED_TOPIC", fetched.ProposalSource);
        Assert.NotNull(fetched.SelectedTopic);
        Assert.Equal(88, fetched.SelectedTopic.Id);
    }

    [Fact]
    public async Task UpdateTopic_ReplacesSelection_WhenDraftEditable()
    {
        ResetProjectRepositoryState();

        var leaderClient = _factory.CreateAuthenticatedClient(1001, roles: [AppRoles.Student]);
        var createResponse = await leaderClient.PostAsJsonAsync("api/v1/projects", new CreateProjectDraftRequest(
            "Proposal", "Desc", "Objs", "Problem", "Output", [100], "Domain", ["Tech"], ["Kw"]));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var project = await createResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);

        var firstSelect = await leaderClient.PutAsJsonAsync(
            $"api/v1/projects/{project.Id}/topic",
            new SelectProjectTopicRequest(50, project.ConcurrencyToken));
        var updated1 = await firstSelect.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(updated1);
        Assert.Equal(50, updated1.TopicId);

        var secondSelect = await leaderClient.PutAsJsonAsync(
            $"api/v1/projects/{project.Id}/topic",
            new SelectProjectTopicRequest(60, updated1.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.OK, secondSelect.StatusCode);
        var updated2 = await secondSelect.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(updated2);
        Assert.Equal(60, updated2.TopicId);
    }

    [Fact]
    public async Task SubmittedProject_TopicChange_Returns409()
    {
        ResetProjectRepositoryState();

        var leaderClient = _factory.CreateAuthenticatedClient(1001, roles: [AppRoles.Student]);
        var createResponse = await leaderClient.PostAsJsonAsync("api/v1/projects", new CreateProjectDraftRequest(
            "Proposal", "Desc", "Objs", "Problem", "Output", [100], "Domain", ["Tech"], ["Kw"]));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var project = await createResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);

        var submitResponse = await leaderClient.PostAsJsonAsync(
            $"api/v1/projects/{project.Id}/submit",
            new SubmitProjectRequest(project.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);
        var submitted = await submitResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(submitted);
        Assert.Equal("SUBMITTED", submitted.Status);

        var changeResponse = await leaderClient.PutAsJsonAsync(
            $"api/v1/projects/{project.Id}/topic",
            new SelectProjectTopicRequest(70, submitted.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.Conflict, changeResponse.StatusCode);
    }

    [Fact]
    public async Task SelectTopic_MissingConcurrencyToken_Returns400()
    {
        ResetProjectRepositoryState();

        var leaderClient = _factory.CreateAuthenticatedClient(1001, roles: [AppRoles.Student]);
        var createResponse = await leaderClient.PostAsJsonAsync("api/v1/projects", new CreateProjectDraftRequest(
            "Proposal", "Desc", "Objs", "Problem", "Output", [100], "Domain", ["Tech"], ["Kw"]));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var project = await createResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);

        var selectResponse = await leaderClient.PutAsJsonAsync(
            $"api/v1/projects/{project.Id}/topic",
            new { topicId = 50 });
        Assert.Equal(HttpStatusCode.BadRequest, selectResponse.StatusCode);
    }

    [Fact]
    public async Task SelectTopic_BlankConcurrencyToken_Returns400()
    {
        ResetProjectRepositoryState();

        var leaderClient = _factory.CreateAuthenticatedClient(1001, roles: [AppRoles.Student]);
        var createResponse = await leaderClient.PostAsJsonAsync("api/v1/projects", new CreateProjectDraftRequest(
            "Proposal", "Desc", "Objs", "Problem", "Output", [100], "Domain", ["Tech"], ["Kw"]));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var project = await createResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);

        var selectResponse = await leaderClient.PutAsJsonAsync(
            $"api/v1/projects/{project.Id}/topic",
            new { topicId = 50, concurrencyToken = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, selectResponse.StatusCode);
    }

    [Fact]
    public async Task SelectTopic_InvalidBase64ConcurrencyToken_Returns400()
    {
        ResetProjectRepositoryState();

        var leaderClient = _factory.CreateAuthenticatedClient(1001, roles: [AppRoles.Student]);
        var createResponse = await leaderClient.PostAsJsonAsync("api/v1/projects", new CreateProjectDraftRequest(
            "Proposal", "Desc", "Objs", "Problem", "Output", [100], "Domain", ["Tech"], ["Kw"]));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var project = await createResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);

        var selectResponse = await leaderClient.PutAsJsonAsync(
            $"api/v1/projects/{project.Id}/topic",
            new { topicId = 50, concurrencyToken = "not-a-base64-token!" });
        Assert.Equal(HttpStatusCode.BadRequest, selectResponse.StatusCode);
    }

    [Fact]
    public async Task SelectTopic_StaleConcurrencyToken_Returns409()
    {
        ResetProjectRepositoryState();

        var leaderClient = _factory.CreateAuthenticatedClient(1001, roles: [AppRoles.Student]);
        var createResponse = await leaderClient.PostAsJsonAsync("api/v1/projects", new CreateProjectDraftRequest(
            "Proposal", "Desc", "Objs", "Problem", "Output", [100], "Domain", ["Tech"], ["Kw"]));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var project = await createResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);

        // First topic selection updates the token
        var firstSelect = await leaderClient.PutAsJsonAsync(
            $"api/v1/projects/{project.Id}/topic",
            new SelectProjectTopicRequest(50, project.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.OK, firstSelect.StatusCode);

        // Second topic selection with original stale token returns 409
        var staleSelect = await leaderClient.PutAsJsonAsync(
            $"api/v1/projects/{project.Id}/topic",
            new SelectProjectTopicRequest(60, project.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.Conflict, staleSelect.StatusCode);
    }

    [Fact]
    public async Task SelectTopic_CurrentConcurrencyToken_Returns200()
    {
        ResetProjectRepositoryState();

        var leaderClient = _factory.CreateAuthenticatedClient(1001, roles: [AppRoles.Student]);
        var createResponse = await leaderClient.PostAsJsonAsync("api/v1/projects", new CreateProjectDraftRequest(
            "Proposal", "Desc", "Objs", "Problem", "Output", [100], "Domain", ["Tech"], ["Kw"]));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var project = await createResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);

        var selectResponse = await leaderClient.PutAsJsonAsync(
            $"api/v1/projects/{project.Id}/topic",
            new SelectProjectTopicRequest(50, project.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.OK, selectResponse.StatusCode);

        var updated = await selectResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(updated);
        Assert.Equal(50, updated.TopicId);
        Assert.Equal("PUBLISHED_TOPIC", updated.ProposalSource);
    }

    [Fact]
    public async Task SelectTopic_TwoConcurrentUpdatesWithoutTokens_BothRejected()
    {
        ResetProjectRepositoryState();

        var leaderClient = _factory.CreateAuthenticatedClient(1001, roles: [AppRoles.Student]);
        var createResponse = await leaderClient.PostAsJsonAsync("api/v1/projects", new CreateProjectDraftRequest(
            "Proposal", "Desc", "Objs", "Problem", "Output", [100], "Domain", ["Tech"], ["Kw"]));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var project = await createResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);

        var task1 = leaderClient.PutAsJsonAsync(
            $"api/v1/projects/{project.Id}/topic",
            new { topicId = 50 });
        var task2 = leaderClient.PutAsJsonAsync(
            $"api/v1/projects/{project.Id}/topic",
            new { topicId = 60 });

        var responses = await Task.WhenAll(task1, task2);
        Assert.Equal(HttpStatusCode.BadRequest, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, responses[1].StatusCode);

        // Verify project remains untouched
        var getResponse = await leaderClient.GetAsync($"api/v1/projects/{project.Id}");
        var finalProject = await getResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(finalProject);
        Assert.Null(finalProject.TopicId);
        Assert.Equal("STUDENT_PROPOSAL", finalProject.ProposalSource);
    }

    [Fact]
    public async Task NonLeader_SelectTopic_Returns403()
    {
        ResetProjectRepositoryState();

        var leaderClient = _factory.CreateAuthenticatedClient(1001, roles: [AppRoles.Student]);
        var memberClient = _factory.CreateAuthenticatedClient(1002, roles: [AppRoles.Student]);

        var createResponse = await leaderClient.PostAsJsonAsync("api/v1/projects", new CreateProjectDraftRequest(
            "Proposal", "Desc", "Objs", "Problem", "Output", [100], "Domain", ["Tech"], ["Kw"]));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var project = await createResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);

        _factory.ProjectRepository.IsLeader = false;
        var response = await memberClient.PutAsJsonAsync(
            $"api/v1/projects/{project.Id}/topic",
            new SelectProjectTopicRequest(50, project.ConcurrencyToken));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CrossProjectLeader_SelectTopic_Returns403()
    {
        ResetProjectRepositoryState();

        var leaderClient = _factory.CreateAuthenticatedClient(1001, roles: [AppRoles.Student]);
        var otherLeaderClient = _factory.CreateAuthenticatedClient(2001, roles: [AppRoles.Student]);

        var createResponse = await leaderClient.PostAsJsonAsync("api/v1/projects", new CreateProjectDraftRequest(
            "Proposal", "Desc", "Objs", "Problem", "Output", [100], "Domain", ["Tech"], ["Kw"]));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var project = await createResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);

        _factory.ProjectRepository.IsLeader = false;
        var response = await otherLeaderClient.PutAsJsonAsync(
            $"api/v1/projects/{project.Id}/topic",
            new SelectProjectTopicRequest(50, project.ConcurrencyToken));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MissingTopic_Returns404()
    {
        ResetProjectRepositoryState();

        var leaderClient = _factory.CreateAuthenticatedClient(1001, roles: [AppRoles.Student]);
        var createResponse = await leaderClient.PostAsJsonAsync("api/v1/projects", new CreateProjectDraftRequest(
            "Proposal", "Desc", "Objs", "Problem", "Output", [100], "Domain", ["Tech"], ["Kw"]));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var project = await createResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);

        _factory.TopicSelectionGuard.OnValidate = (topicId, projId, userId, ct) =>
            throw new NotFoundException("Topic", topicId);

        var response = await leaderClient.PutAsJsonAsync(
            $"api/v1/projects/{project.Id}/topic",
            new SelectProjectTopicRequest(999, project.ConcurrencyToken));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ClosedOrDraftTopic_Returns409()
    {
        ResetProjectRepositoryState();

        var leaderClient = _factory.CreateAuthenticatedClient(1001, roles: [AppRoles.Student]);
        var createResponse = await leaderClient.PostAsJsonAsync("api/v1/projects", new CreateProjectDraftRequest(
            "Proposal", "Desc", "Objs", "Problem", "Output", [100], "Domain", ["Tech"], ["Kw"]));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var project = await createResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);

        _factory.TopicSelectionGuard.OnValidate = (topicId, projId, userId, ct) =>
            throw new ConflictException("TOPIC_NOT_PUBLISHED");

        var response = await leaderClient.PutAsJsonAsync(
            $"api/v1/projects/{project.Id}/topic",
            new SelectProjectTopicRequest(999, project.ConcurrencyToken));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SelectTopic_NoRegistrationWindow_Returns409_REGISTRATION_WINDOW_UNAVAILABLE()
    {
        ResetProjectRepositoryState();

        var leaderClient = _factory.CreateAuthenticatedClient(1001, roles: [AppRoles.Student]);
        var createResponse = await leaderClient.PostAsJsonAsync("api/v1/projects", new CreateProjectDraftRequest(
            "Proposal", "Desc", "Objs", "Problem", "Output", [100], "Domain", ["Tech"], ["Kw"]));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var project = await createResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);

        _factory.TopicSelectionGuard.OnValidate = (topicId, projId, userId, ct) =>
            throw new ConflictException("REGISTRATION_WINDOW_UNAVAILABLE");

        var response = await leaderClient.PutAsJsonAsync(
            $"api/v1/projects/{project.Id}/topic",
            new SelectProjectTopicRequest(10, project.ConcurrencyToken));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("REGISTRATION_WINDOW_UNAVAILABLE", content);
    }

    [Fact]
    public async Task SelectTopic_LegacyTeamWrongMajor_Returns409_PRIMARY_MAJOR_MISMATCH()
    {
        ResetProjectRepositoryState();

        var leaderClient = _factory.CreateAuthenticatedClient(1001, roles: [AppRoles.Student]);
        var createResponse = await leaderClient.PostAsJsonAsync("api/v1/projects", new CreateProjectDraftRequest(
            "Proposal", "Desc", "Objs", "Problem", "Output", [100], "Domain", ["Tech"], ["Kw"]));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var project = await createResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);

        _factory.TopicSelectionGuard.OnValidate = (topicId, projId, userId, ct) =>
            throw new ConflictException("PRIMARY_MAJOR_MISMATCH");

        var response = await leaderClient.PutAsJsonAsync(
            $"api/v1/projects/{project.Id}/topic",
            new SelectProjectTopicRequest(10, project.ConcurrencyToken));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("PRIMARY_MAJOR_MISMATCH", content);
    }

    [Fact]
    public async Task SelectTopic_InterdisciplinaryNoMajorEvidence_Returns409()
    {
        ResetProjectRepositoryState();

        var leaderClient = _factory.CreateAuthenticatedClient(1001, roles: [AppRoles.Student]);
        var createResponse = await leaderClient.PostAsJsonAsync("api/v1/projects", new CreateProjectDraftRequest(
            "Proposal", "Desc", "Objs", "Problem", "Output", [100], "Domain", ["Tech"], ["Kw"]));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var project = await createResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);

        _factory.TopicSelectionGuard.OnValidate = (topicId, projId, userId, ct) =>
            throw new ConflictException("MAJOR_EVIDENCE_UNAVAILABLE");

        var response = await leaderClient.PutAsJsonAsync(
            $"api/v1/projects/{project.Id}/topic",
            new SelectProjectTopicRequest(10, project.ConcurrencyToken));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("MAJOR_EVIDENCE_UNAVAILABLE", content);
    }

    [Fact]
    public async Task SelectTopic_InterdisciplinaryBelowMinQuota_Returns409()
    {
        ResetProjectRepositoryState();

        var leaderClient = _factory.CreateAuthenticatedClient(1001, roles: [AppRoles.Student]);
        var createResponse = await leaderClient.PostAsJsonAsync("api/v1/projects", new CreateProjectDraftRequest(
            "Proposal", "Desc", "Objs", "Problem", "Output", [100], "Domain", ["Tech"], ["Kw"]));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var project = await createResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);

        _factory.TopicSelectionGuard.OnValidate = (topicId, projId, userId, ct) =>
            throw new ConflictException("MAJOR_MIN_MEMBERS");

        var response = await leaderClient.PutAsJsonAsync(
            $"api/v1/projects/{project.Id}/topic",
            new SelectProjectTopicRequest(10, project.ConcurrencyToken));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("MAJOR_MIN_MEMBERS", content);
    }

    [Fact]
    public async Task SelectTopic_InterdisciplinaryAboveMaxQuota_Returns409()
    {
        ResetProjectRepositoryState();

        var leaderClient = _factory.CreateAuthenticatedClient(1001, roles: [AppRoles.Student]);
        var createResponse = await leaderClient.PostAsJsonAsync("api/v1/projects", new CreateProjectDraftRequest(
            "Proposal", "Desc", "Objs", "Problem", "Output", [100], "Domain", ["Tech"], ["Kw"]));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var project = await createResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);

        _factory.TopicSelectionGuard.OnValidate = (topicId, projId, userId, ct) =>
            throw new ConflictException("MAJOR_MAX_MEMBERS");

        var response = await leaderClient.PutAsJsonAsync(
            $"api/v1/projects/{project.Id}/topic",
            new SelectProjectTopicRequest(10, project.ConcurrencyToken));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("MAJOR_MAX_MEMBERS", content);
    }

    [Fact]
    public async Task SelectTopic_InterdisciplinaryValidQuotas_Succeeds()
    {
        ResetProjectRepositoryState();

        var leaderClient = _factory.CreateAuthenticatedClient(1001, roles: [AppRoles.Student]);
        var createResponse = await leaderClient.PostAsJsonAsync("api/v1/projects", new CreateProjectDraftRequest(
            "Proposal", "Desc", "Objs", "Problem", "Output", [100], "Domain", ["Tech"], ["Kw"]));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var project = await createResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);

        var response = await leaderClient.PutAsJsonAsync(
            $"api/v1/projects/{project.Id}/topic",
            new SelectProjectTopicRequest(10, project.ConcurrencyToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(updated);
        Assert.Equal(10, updated.TopicId);
    }

    [Fact]
    public async Task SubmitPublishedTopicDraft_RevalidatesCurrentTopicRules()
    {
        ResetProjectRepositoryState();

        var leaderClient = _factory.CreateAuthenticatedClient(1001, roles: [AppRoles.Student]);
        var createResponse = await leaderClient.PostAsJsonAsync("api/v1/projects", new CreateProjectDraftRequest(
            "Proposal", "Desc", "Objs", "Problem", "Output", [100], "Domain", ["Tech"], ["Kw"]));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var project = await createResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);

        // Select topic
        var selectResponse = await leaderClient.PutAsJsonAsync(
            $"api/v1/projects/{project.Id}/topic",
            new SelectProjectTopicRequest(50, project.ConcurrencyToken));
        project = await selectResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);
        Assert.Equal("PUBLISHED_TOPIC", project.ProposalSource);

        bool validatedOnSubmit = false;
        _factory.TopicSelectionGuard.OnValidate = (topicId, projId, userId, ct) =>
        {
            validatedOnSubmit = true;
            return Task.CompletedTask;
        };

        // Submit
        var submitResponse = await leaderClient.PostAsJsonAsync(
            $"api/v1/projects/{project.Id}/submit",
            new SubmitProjectRequest(project.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);
        Assert.True(validatedOnSubmit, "Topic rules should have been revalidated on submit!");

        var submitted = await submitResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(submitted);
        Assert.Equal("SUBMITTED", submitted.Status);
    }

    [Fact]
    public async Task SubmitPublishedTopicDraft_WhenRulesBecomeInvalid_Returns409()
    {
        ResetProjectRepositoryState();

        var leaderClient = _factory.CreateAuthenticatedClient(1001, roles: [AppRoles.Student]);
        var createResponse = await leaderClient.PostAsJsonAsync("api/v1/projects", new CreateProjectDraftRequest(
            "Proposal", "Desc", "Objs", "Problem", "Output", [100], "Domain", ["Tech"], ["Kw"]));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var project = await createResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);

        // Select topic initially passes
        var selectResponse = await leaderClient.PutAsJsonAsync(
            $"api/v1/projects/{project.Id}/topic",
            new SelectProjectTopicRequest(50, project.ConcurrencyToken));
        project = await selectResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);

        // Rules become invalid at submit time
        _factory.TopicSelectionGuard.OnValidate = (topicId, projId, userId, ct) =>
            throw new ConflictException("MAJOR_MIN_MEMBERS");

        var submitResponse = await leaderClient.PostAsJsonAsync(
            $"api/v1/projects/{project.Id}/submit",
            new SubmitProjectRequest(project.ConcurrencyToken));

        Assert.Equal(HttpStatusCode.Conflict, submitResponse.StatusCode);
        var content = await submitResponse.Content.ReadAsStringAsync();
        Assert.Contains("MAJOR_MIN_MEMBERS", content);

        // Verify project remains in DRAFT
        var getResponse = await leaderClient.GetAsync($"api/v1/projects/{project.Id}");
        var current = await getResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(current);
        Assert.Equal("DRAFT", current.Status);
    }

    [Fact]
    public async Task SubmitStudentProposalDraft_RemainsSupported()
    {
        ResetProjectRepositoryState();

        var leaderClient = _factory.CreateAuthenticatedClient(1001, roles: [AppRoles.Student]);
        var createResponse = await leaderClient.PostAsJsonAsync("api/v1/projects", new CreateProjectDraftRequest(
            "Student Proposal Project", "Desc", "Objs", "Problem", "Output", [100], "Domain", ["Tech"], ["Kw"]));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var project = await createResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(project);
        Assert.Equal("STUDENT_PROPOSAL", project.ProposalSource);
        Assert.Null(project.TopicId);

        var submitResponse = await leaderClient.PostAsJsonAsync(
            $"api/v1/projects/{project.Id}/submit",
            new SubmitProjectRequest(project.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        var submitted = await submitResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(submitted);
        Assert.Equal("SUBMITTED", submitted.Status);
        Assert.Equal("STUDENT_PROPOSAL", submitted.ProposalSource);
        Assert.Null(submitted.TopicId);
    }

    private async Task<(HttpClient leaderClient, HttpClient staffClient, ProjectDto project)> SetupProjectInRevisionRequiredAsync(
        bool isPublishedTopic = true,
        long topicId = 50)
    {
        ResetProjectRepositoryState();
        _factory.TopicSelectionGuard.OnValidate = null;

        var leaderClient = _factory.CreateAuthenticatedClient(1001, roles: [AppRoles.Student]);
        long staffUserId = 2001;
        _factory.AcademicRepository.Scopes[staffUserId] = new AcademicUserScope(1, 100);
        if (!_factory.ProjectRepository.ProjectDeptIds.Contains(100))
        {
            _factory.ProjectRepository.ProjectDeptIds.Add(100);
        }
        var staffClient = _factory.CreateAuthenticatedClient(staffUserId, "staff@aipms.test", "Staff", AppRoles.DepartmentStaff);

        var createResponse = await leaderClient.PostAsJsonAsync("api/v1/projects", new CreateProjectDraftRequest(
            "Revision Project", "Desc", "Objs", "Problem", "Output", [100], "Domain", ["Tech"], ["Kw"]));
        var project = (await createResponse.Content.ReadFromJsonAsync<ProjectDto>())!;

        if (isPublishedTopic)
        {
            var selectResponse = await leaderClient.PutAsJsonAsync(
                $"api/v1/projects/{project.Id}/topic",
                new SelectProjectTopicRequest(topicId, project.ConcurrencyToken));
            project = (await selectResponse.Content.ReadFromJsonAsync<ProjectDto>())!;
        }

        var submitResponse = await leaderClient.PostAsJsonAsync(
            $"api/v1/projects/{project.Id}/submit",
            new SubmitProjectRequest(project.ConcurrencyToken));
        project = (await submitResponse.Content.ReadFromJsonAsync<ProjectDto>())!;

        var startReviewResponse = await staffClient.PostAsJsonAsync(
            $"api/v1/projects/{project.Id}/start-review",
            new SubmitProjectRequest(project.ConcurrencyToken));
        project = (await startReviewResponse.Content.ReadFromJsonAsync<ProjectDto>())!;

        var revisionResponse = await staffClient.PostAsJsonAsync(
            $"api/v1/projects/{project.Id}/revision",
            new ProjectReviewRequest(project.ConcurrencyToken, "Please revise requirements."));
        project = (await revisionResponse.Content.ReadFromJsonAsync<ProjectDto>())!;

        return (leaderClient, staffClient, project);
    }

    [Fact]
    public async Task ResubmitPublishedTopic_WhenTopicBecomesInvalid_Returns409AndRemainsRevisionRequired()
    {
        var (leaderClient, _, project) = await SetupProjectInRevisionRequiredAsync(isPublishedTopic: true, topicId: 50);
        Assert.Equal("REVISION_REQUIRED", project.Status);
        Assert.Equal(50, project.TopicId);
        Assert.Equal("PUBLISHED_TOPIC", project.ProposalSource);

        var preHistoriesCount = _factory.ProjectRepository.StatusHistories.GetValueOrDefault(project.Id)?.Count ?? 0;
        var preEventsCount = _factory.Notifications.Events.Count;

        // Invalidate topic before resubmit (e.g. topic is closed/unpublished)
        _factory.TopicSelectionGuard.OnValidate = (topicId, projId, userId, ct) =>
            throw new ConflictException("Only published topics can be selected.");

        var resubmitResponse = await leaderClient.PostAsJsonAsync(
            $"api/v1/projects/{project.Id}/resubmit",
            new SubmitProjectRequest(project.ConcurrencyToken));

        Assert.Equal(HttpStatusCode.Conflict, resubmitResponse.StatusCode);
        var content = await resubmitResponse.Content.ReadAsStringAsync();
        Assert.Contains("Only published topics can be selected.", content);

        // Verify project remains REVISION_REQUIRED
        var getResponse = await leaderClient.GetAsync($"api/v1/projects/{project.Id}");
        var current = await getResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(current);
        Assert.Equal("REVISION_REQUIRED", current.Status);
        Assert.Equal(50, current.TopicId);
        Assert.Equal("PUBLISHED_TOPIC", current.ProposalSource);

        // No new status history added for SUBMITTED
        var postHistories = _factory.ProjectRepository.StatusHistories.GetValueOrDefault(project.Id) ?? [];
        Assert.DoesNotContain(postHistories.Skip(preHistoriesCount), h => h.NewStatus == "SUBMITTED");

        // No new notification emitted
        Assert.Equal(preEventsCount, _factory.Notifications.Events.Count);
    }

    [Fact]
    public async Task ResubmitPublishedTopic_WhenTopicStillValid_Succeeds()
    {
        var (leaderClient, _, project) = await SetupProjectInRevisionRequiredAsync(isPublishedTopic: true, topicId: 50);
        bool validated = false;
        _factory.TopicSelectionGuard.OnValidate = (topicId, projId, userId, ct) =>
        {
            validated = true;
            return Task.CompletedTask;
        };

        var resubmitResponse = await leaderClient.PostAsJsonAsync(
            $"api/v1/projects/{project.Id}/resubmit",
            new SubmitProjectRequest(project.ConcurrencyToken));

        Assert.Equal(HttpStatusCode.OK, resubmitResponse.StatusCode);
        Assert.True(validated, "Topic rules should have been revalidated on resubmit!");

        var current = await resubmitResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(current);
        Assert.Equal("SUBMITTED", current.Status);
        Assert.Equal(50, current.TopicId);
        Assert.Equal("PUBLISHED_TOPIC", current.ProposalSource);
    }

    [Fact]
    public async Task ResubmitPublishedTopic_WhenRegistrationWindowUnavailable_Returns409()
    {
        var (leaderClient, _, project) = await SetupProjectInRevisionRequiredAsync(isPublishedTopic: true, topicId: 50);

        _factory.TopicSelectionGuard.OnValidate = (topicId, projId, userId, ct) =>
            throw new ConflictException("REGISTRATION_WINDOW_UNAVAILABLE");

        var resubmitResponse = await leaderClient.PostAsJsonAsync(
            $"api/v1/projects/{project.Id}/resubmit",
            new SubmitProjectRequest(project.ConcurrencyToken));

        Assert.Equal(HttpStatusCode.Conflict, resubmitResponse.StatusCode);
        var content = await resubmitResponse.Content.ReadAsStringAsync();
        Assert.Contains("REGISTRATION_WINDOW_UNAVAILABLE", content);

        var current = await (await leaderClient.GetAsync($"api/v1/projects/{project.Id}")).Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(current);
        Assert.Equal("REVISION_REQUIRED", current.Status);
    }

    [Fact]
    public async Task ResubmitPublishedTopic_WhenTeamEligibilityInvalid_Returns409()
    {
        var (leaderClient, _, project) = await SetupProjectInRevisionRequiredAsync(isPublishedTopic: true, topicId: 50);

        _factory.TopicSelectionGuard.OnValidate = (topicId, projId, userId, ct) =>
            throw new ConflictException("PRIMARY_MAJOR_MISMATCH");

        var resubmitResponse = await leaderClient.PostAsJsonAsync(
            $"api/v1/projects/{project.Id}/resubmit",
            new SubmitProjectRequest(project.ConcurrencyToken));

        Assert.Equal(HttpStatusCode.Conflict, resubmitResponse.StatusCode);
        var content = await resubmitResponse.Content.ReadAsStringAsync();
        Assert.Contains("PRIMARY_MAJOR_MISMATCH", content);

        var current = await (await leaderClient.GetAsync($"api/v1/projects/{project.Id}")).Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(current);
        Assert.Equal("REVISION_REQUIRED", current.Status);
    }

    [Fact]
    public async Task ResubmitStudentProposal_RemainsSupportedWithoutTopicSelectionGuard()
    {
        var (leaderClient, _, project) = await SetupProjectInRevisionRequiredAsync(isPublishedTopic: false);
        Assert.Equal("STUDENT_PROPOSAL", project.ProposalSource);
        Assert.Null(project.TopicId);

        _factory.TopicSelectionGuard.OnValidate = (topicId, projId, userId, ct) =>
            throw new InvalidOperationException("TopicSelectionGuard should NOT be called for STUDENT_PROPOSAL");

        var resubmitResponse = await leaderClient.PostAsJsonAsync(
            $"api/v1/projects/{project.Id}/resubmit",
            new SubmitProjectRequest(project.ConcurrencyToken));

        Assert.Equal(HttpStatusCode.OK, resubmitResponse.StatusCode);
        var current = await resubmitResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(current);
        Assert.Equal("SUBMITTED", current.Status);
        Assert.Equal("STUDENT_PROPOSAL", current.ProposalSource);
        Assert.Null(current.TopicId);
    }
}

public sealed class StubTopicSelectionGuard : ITopicSelectionGuard
{
    public Func<long, long, long, CancellationToken, Task>? OnValidate { get; set; }

    public Task ValidateTopicSelectionAsync(long topicId, long projectId, long actorUserId, CancellationToken cancellationToken)
    {
        if (OnValidate is not null)
        {
            return OnValidate(topicId, projectId, actorUserId, cancellationToken);
        }
        return Task.CompletedTask;
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
    public Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct) => action(ct);
    public Task<ProjectAcademicReviewDto> GetAcademicReviewAsync(long projectId, CancellationToken ct) => throw new NotSupportedException();
    public Task<ProjectAcademicReviewDto> RecordDepartmentDecisionAsync(long projectId, long actorId,
        DepartmentDecisionRequest request, CancellationToken ct) => throw new NotSupportedException();
    public Task<PagedResult<ProjectSummaryDto>> GetVisibleProjectsAsync(long userId, string? status, long? teamId,
        long? semesterId, long? majorId, string? tag, string? search, int page, int pageSize, CancellationToken ct) =>
        GetProjectsAsync(status, teamId, semesterId, majorId, tag, search, page, pageSize, ct);

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

    public Task LockProjectAndTopicAsync(long projectId, long topicId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<ProjectDto> SelectTopicAsync(
        long projectId,
        long topicId,
        string concurrencyToken,
        CancellationToken cancellationToken)
    {
        var existing = Projects[projectId];
        if (existing.Status != "DRAFT" && existing.Status != "REVISION_REQUIRED")
        {
            throw new AIPMS.Application.Common.Exceptions.ConflictException("Only an editable proposal can be updated.");
        }
        if (string.IsNullOrWhiteSpace(concurrencyToken))
        {
            throw new ArgumentException("Concurrency token is required.", nameof(concurrencyToken));
        }
        if (existing.ConcurrencyToken != concurrencyToken)
        {
            throw new AIPMS.Application.Common.Exceptions.ConflictException("Concurrency token mismatch.");
        }
        var updated = existing with
        {
            TopicId = topicId,
            ProposalSource = "PUBLISHED_TOPIC",
            ConcurrencyToken = Convert.ToBase64String(BitConverter.GetBytes((long)(projectId + 10))),
            SelectedTopic = new SelectedTopicDto(topicId, "TOPIC-" + topicId, "Selected topic " + topicId)
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
