using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using AIPMS.Application.Features.Topics.DTOs;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Topics;

public sealed class TopicAiSimilarityEndpointTests(TopicDatabaseFixture database) : IClassFixture<TopicDatabaseFixture>
{
    private const string Endpoint = "/api/v1/topics/ai-similarity-check";
    private static TopicAiSimilarityRequest Input(long team) => new(team, "Local commerce platform",
        "Help local shops sell online", "Manage products and orders", "A working web application", "Commerce", ["commerce"]);

    [Fact]
    public async Task Leadership_revoked_during_model_call_cannot_receive_result()
    {
        var s = await database.Seed(); var team = await Team(s);
        await using (var db = database.CreateContext()) { db.Add(Topic(s, "Commerce catalogue", "Public text")); await db.SaveChangesAsync(); }
        var provider = new CaptureProvider(async () =>
        {
            await using var db = database.CreateContext();
            var membership = await db.TeamMembers.SingleAsync(m => m.TeamId == team && m.UserId == s.Users.Student);
            membership.IsLeader = false; await db.SaveChangesAsync();
        });
        using var app = new Factory(database, provider); using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        Assert.Equal(HttpStatusCode.Forbidden, (await leader.PostAsJsonAsync(Endpoint, Input(team))).StatusCode);
        Assert.NotNull(provider.Prompt);
    }

    [Fact]
    public async Task Requires_authenticated_current_leader_and_persisted_eligible_student()
    {
        var s = await database.Seed(); var team = await Team(s); var provider = new CaptureProvider();
        using var app = new Factory(database, provider);
        using var anonymous = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(Endpoint, Input(team))).StatusCode);
        using var outsider = app.CreateAuthenticatedClient(s.OtherStudent);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PostAsJsonAsync(Endpoint, Input(team))).StatusCode);
        using var forgedStudent = app.CreateAuthenticatedClient(s.Users.Lecturer, roles: ["STUDENT"]);
        Assert.Equal(HttpStatusCode.Forbidden, (await forgedStudent.PostAsJsonAsync(Endpoint, Input(team))).StatusCode);
        await using var db = database.CreateContext();
        var actor = (await db.Users.FindAsync(s.Users.Student))!;
        actor.AcademicProfileStatus = "PENDING"; await db.SaveChangesAsync();
        using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        Assert.Equal(HttpStatusCode.Forbidden, (await leader.PostAsJsonAsync(Endpoint, Input(team))).StatusCode);
        actor.AcademicProfileStatus = "VERIFIED"; actor.Status = "INACTIVE"; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await leader.PostAsJsonAsync(Endpoint, Input(team))).StatusCode);
        Assert.Null(provider.Prompt);
    }

    [Fact]
    public async Task Invalid_content_and_foreign_project_are_rejected_before_provider()
    {
        var s = await database.Seed(); var team = await Team(s); var provider = new CaptureProvider();
        using var app = new Factory(database, provider); using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        Assert.Equal(HttpStatusCode.BadRequest, (await leader.PostAsJsonAsync(Endpoint, Input(team) with { Title = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await leader.PostAsJsonAsync(Endpoint, Input(team) with { ProjectId = long.MaxValue })).StatusCode);
        Assert.Null(provider.Prompt);
    }

    [Fact]
    public async Task Pre_draft_review_uses_scoped_catalogue_and_private_projects_metadata_only_with_truthful_fallback()
    {
        var s = await database.Seed(); var team = await Team(s);
        var other = await database.Seed();
        await using (var db = database.CreateContext())
        {
            db.AddRange(Topic(s, "Visible commerce catalogue", "PUBLIC_CATALOGUE_EVIDENCE"),
                Topic(s, "Hidden draft", "SECRET_DRAFT", "DRAFT"),
                Topic(s, "Other department", "SECRET_OTHER_DEPARTMENT", department: s.OtherDepartment),
                Topic(other, "Other organization", "SECRET_OTHER_ORGANIZATION"));
            var privateTeam = new M.Team { AcademicSemesterId = s.Semester, Code = Guid.NewGuid().ToString("N"),
                Name = "Other team", Status = "FORMING", CreatedBy = s.Users.Student,
                CreatedAt = TopicDatabaseFixture.Now, UpdatedAt = TopicDatabaseFixture.Now };
            db.Projects.Add(new M.Project { Team = privateTeam, Code = Guid.NewGuid().ToString("N"),
                Title = "Other commerce project", ProblemStatement = "SECRET_PROJECT_PROBLEM", Objectives = "SECRET_PROJECT_OBJECTIVES",
                ExpectedOutput = "SECRET_PROJECT_OUTPUT", Status = "ACTIVE", CreatedBy = s.Users.Student,
                CreatedAt = TopicDatabaseFixture.Now, UpdatedAt = TopicDatabaseFixture.Now, RegisteredAt = TopicDatabaseFixture.Now,
                ProjectMajors = [new M.ProjectMajor { MajorId = s.Major }] });
            await db.SaveChangesAsync();
        }
        var provider = new CaptureProvider(); using var app = new Factory(database, provider);
        using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        var response = await leader.PostAsJsonAsync(Endpoint, Input(team));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = (await response.Content.ReadFromJsonAsync<TopicAiSimilarityResultDto>())!;
        Assert.Equal("FALLBACK", result.GenerationStatus);
        Assert.Equal(2, result.CandidateCount);
        Assert.Contains("PUBLIC_CATALOGUE_EVIDENCE", provider.Prompt);
        Assert.Contains("Other commerce project", provider.Prompt);
        Assert.DoesNotContain("SECRET_", provider.Prompt);
        await using var check = database.CreateContext();
        Assert.False(await check.Projects.AnyAsync(p => p.TeamId == team));
        Assert.Contains(await check.AuditLogs.Where(a => a.Action == "TOPIC_AI_SIMILARITY_CHECKED").ToArrayAsync(),
            a => a.EntityId == team.ToString());
    }

    [Fact]
    public async Task Automatically_excludes_own_project_and_selected_topic()
    {
        var s = await database.Seed(); var team = await Team(s);
        await using (var db = database.CreateContext())
        {
            var topic = Topic(s, "My selected topic", "OWN_SELECTED_TOPIC"); db.Add(topic); await db.SaveChangesAsync();
            db.Projects.Add(new M.Project { TeamId = team, TopicId = topic.Id, Code = Guid.NewGuid().ToString("N"),
                Title = "My own project", Status = "ACTIVE", CreatedBy = s.Users.Student,
                CreatedAt = TopicDatabaseFixture.Now, UpdatedAt = TopicDatabaseFixture.Now, RegisteredAt = TopicDatabaseFixture.Now,
                ProjectMajors = [new M.ProjectMajor { MajorId = s.Major }] });
            await db.SaveChangesAsync();
        }
        var provider = new CaptureProvider(); using var app = new Factory(database, provider);
        using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        var response = await leader.PostAsJsonAsync(Endpoint, Input(team));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = (await response.Content.ReadFromJsonAsync<TopicAiSimilarityResultDto>())!;
        Assert.Equal(0, result.CandidateCount); Assert.Null(result.ExactMatch); Assert.Null(provider.Prompt);
    }

    [Fact]
    public async Task Grounded_provider_response_is_returned_as_completed_advisory_match()
    {
        var s = await database.Seed(); var team = await Team(s);
        await using (var db = database.CreateContext())
        {
            db.Add(Topic(s, "Visible commerce catalogue", "Public catalogue evidence"));
            await db.SaveChangesAsync();
        }
        var response = "{\"matches\":[{\"key\":1,\"level\":\"MEDIUM\",\"reason\":\"Cùng hỗ trợ bán hàng địa phương nhưng phạm vi khác nhau.\",\"sharedAspects\":[\"Bán hàng địa phương\"],\"differences\":[\"Khác quy trình triển khai\"],\"candidateExcerpt\":\"Visible commerce catalogue\",\"proposalExcerpt\":\"Local commerce platform\"}]}";
        var provider = new CaptureProvider(response: response); using var app = new Factory(database, provider);
        using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        var http = await leader.PostAsJsonAsync(Endpoint, Input(team));
        Assert.True(http.IsSuccessStatusCode, await http.Content.ReadAsStringAsync());
        var result = (await http.Content.ReadFromJsonAsync<TopicAiSimilarityResultDto>())!;
        var match = Assert.Single(result.Matches);
        Assert.Equal("COMPLETED", result.GenerationStatus);
        Assert.Equal("MEDIUM", match.SimilarityLevel);
        Assert.Equal("Visible commerce catalogue", match.CandidateExcerpt);
        Assert.Equal("Local commerce platform", match.ProposalExcerpt);
    }

    private async Task<long> Team(TopicScenario s)
    {
        await using var db = database.CreateContext();
        var team = new M.Team { AcademicSemesterId = s.Semester, Code = Guid.NewGuid().ToString("N"), Name = "AI check team",
            Status = "FORMING", CreatedBy = s.Users.Student, CreatedAt = TopicDatabaseFixture.Now, UpdatedAt = TopicDatabaseFixture.Now,
            TeamMembers = [new M.TeamMember { UserId = s.Users.Student, AcademicSemesterId = s.Semester, IsLeader = true,
                JoinedAt = TopicDatabaseFixture.Now, CreatedAt = TopicDatabaseFixture.Now, UpdatedAt = TopicDatabaseFixture.Now }] };
        db.Teams.Add(team); await db.SaveChangesAsync(); return team.Id;
    }

    private static ProjectTopic Topic(TopicScenario s, string title, string problem, string status = "PUBLISHED", long? department = null) =>
        new() { ProjectPeriodId = s.Period, LeadDepartmentId = department ?? s.Users.DepartmentId,
            Code = Guid.NewGuid().ToString("N"), Title = title, ProblemStatement = problem, Objectives = "Commerce workflow",
            ExpectedOutput = "Web application", Domain = "Commerce", KeywordsJson = "[\"commerce\"]", Status = status,
            ProjectMode = "SINGLE_MAJOR", PrimaryMajorId = s.Major, CreatedBy = s.Users.Staff, UpdatedBy = s.Users.Staff,
            CreatedAt = TopicDatabaseFixture.Now, UpdatedAt = TopicDatabaseFixture.Now, ConcurrencyToken = Guid.NewGuid(),
            PublishedBy = status == "PUBLISHED" ? s.Users.Staff : null,
            PublishedAt = status == "PUBLISHED" ? TopicDatabaseFixture.Now : null };

    private sealed class CaptureProvider(Func<Task>? duringCall = null, string response = "invalid-json-for-fallback") : IAiTextGenerationProvider
    {
        public string ModelVersion => "TEST_SEMANTIC";
        public string? Prompt { get; private set; }
        public async Task<string> GenerateTextAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
        { Prompt = userPrompt; if (duringCall is not null) await duringCall(); return response; }
    }

    private sealed class Factory(TopicDatabaseFixture database, IAiTextGenerationProvider provider) : AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = database.ConnectionString,
                ["AiAssistant:Enabled"] = "true",
                ["AiAssistant:TopicSimilarityEnabled"] = "true"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAiTextGenerationProvider>(); services.AddSingleton(provider);
                services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(new FixedClock());
            });
        }
    }
    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(TopicDatabaseFixture.Now); }
}
