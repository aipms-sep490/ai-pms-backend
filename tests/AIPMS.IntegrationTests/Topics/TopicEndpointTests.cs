using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Topics.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.Infrastructure.Services.Auditing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AIPMS.IntegrationTests.Topics;

public sealed class TopicEndpointTests(TopicDatabaseFixture database) : IClassFixture<TopicDatabaseFixture>
{
    private static TopicContentRequest Content(TopicScenario s, bool hybrid = false) => new("Capstone catalogue", "Description",
        "Problem", "Objectives", "Expected output", "Education", ["Dotnet"], ["Capstone"],
        hybrid ? "INTERDISCIPLINARY" : "SINGLE_MAJOR", hybrid ? null : s.Major,
        hybrid ? [new(s.Major, 1, 3, "Software"), new(s.OtherMajor, 1, 2, "Business analysis")]
            : [new(s.Major, 2, 5, "Software")]);

    private static CreateTopicRequest Input(TopicScenario s, bool hybrid = false, string code = "TOPIC") =>
        new(s.Period, code, s.Users.DepartmentId, Content(s, hybrid));

    private static async Task<T> Body<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static Task<TopicDto> Create(HttpClient client, CreateTopicRequest input) =>
        Post(client, "/api/v1/topics", input);
    private static async Task<TopicDto> Post<T>(HttpClient client, string url, T input) =>
        await Body<TopicDto>(await client.PostAsJsonAsync(url, input));
    private static Task<TopicDto> Publish(HttpClient client, TopicDto topic) =>
        Post(client, $"/api/v1/topics/{topic.Id}/publish", new PublishTopicRequest(topic.ConcurrencyToken));

    [Fact]
    public async Task Lecturer_draft_staff_publication_student_discovery_and_close_follow_lifecycle()
    {
        var s = await database.Seed(); using var app = new Factory(database);
        using var lecturer = app.CreateAuthenticatedClient(s.Users.Lecturer, roles: ["LECTURER"]);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff, roles: ["DEPARTMENT_STAFF"]);
        using var student = app.CreateAuthenticatedClient(s.Users.Student, roles: ["STUDENT"]);
        using var peer = app.CreateAuthenticatedClient(s.Users.NewLecturer, roles: ["LECTURER"]);
        var topic = await Create(lecturer, Input(s));
        Assert.Equal("DRAFT", topic.Status);
        Assert.Equal(HttpStatusCode.NotFound, (await student.GetAsync($"/api/v1/topics/{topic.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await peer.GetAsync($"/api/v1/topics/{topic.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await lecturer.PostAsJsonAsync($"/api/v1/topics/{topic.Id}/publish", new PublishTopicRequest(topic.ConcurrencyToken))).StatusCode);
        topic = await Body<TopicDto>(await staff.PutAsJsonAsync($"/api/v1/topics/{topic.Id}", new UpdateTopicRequest(topic.ConcurrencyToken,
            Content(s) with { Title = "Reviewed title" })));
        topic = await Publish(staff, topic);
        Assert.Equal("PUBLISHED", topic.Status);
        Assert.Equal(s.Users.Staff, topic.PublishedBy);
        var response = await student.GetAsync($"/api/v1/topics/{topic.Id}");
        var visible = await Body<TopicDto>(response);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.True(visible.MatchesMyMajor);
        Assert.Equal("Reviewed title", visible.Title);
        Assert.Equal(HttpStatusCode.Conflict, (await lecturer.PutAsJsonAsync($"/api/v1/topics/{topic.Id}", new UpdateTopicRequest(topic.ConcurrencyToken, Content(s)))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await lecturer.PostAsJsonAsync($"/api/v1/topics/{topic.Id}/close", new CloseTopicRequest(topic.ConcurrencyToken, "Withdraw"))).StatusCode);
        topic = await Post(staff, $"/api/v1/topics/{topic.Id}/close", new CloseTopicRequest(topic.ConcurrencyToken, "No longer offered"));
        Assert.Equal("CLOSED", topic.Status); Assert.NotNull(topic.PublishedAt);
        Assert.Equal(HttpStatusCode.NotFound, (await student.GetAsync($"/api/v1/topics/{topic.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync($"/api/v1/topics/{topic.Id}/publish", new PublishTopicRequest(topic.ConcurrencyToken))).StatusCode);
        await using var db = database.CreateContext();
        Assert.Equal(4, await db.AuditLogs.CountAsync(a => a.EntityType == "PROJECT_TOPIC" && a.EntityId == topic.Id.ToString()));
        Assert.Empty(await db.Projects.Where(p => p.Team.AcademicSemesterId == s.Semester).ToArrayAsync());
    }

    [Fact]
    public async Task Hybrid_filters_and_pagination_use_authorized_catalogue_and_verified_major()
    {
        var s = await database.Seed(); var foreign = await database.Seed(); using var app = new Factory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff, roles: ["DEPARTMENT_STAFF"]);
        using var otherStaff = app.CreateAuthenticatedClient(foreign.Users.Staff, roles: ["DEPARTMENT_STAFF"]);
        using var business = app.CreateAuthenticatedClient(s.OtherStudent, roles: ["STUDENT"]);
        var hybrid = await Publish(staff, await Create(staff, Input(s, true, "HYBRID")));
        var single = await Publish(staff, await Create(staff, Input(s, false, "SINGLE")));
        await Create(staff, Input(s, true, "HIDDEN"));
        var hidden = await Publish(otherStaff, await Create(otherStaff, Input(foreign, true)));
        var page = await Body<PagedResult<TopicDto>>(await business.GetAsync($"/api/v1/topics?academicSemesterId={s.Semester}&compatibleOnly=true&pageSize=1"));
        Assert.Equal(1, page.TotalCount); Assert.Equal(hybrid.Id, Assert.Single(page.Items).Id);
        page = await Body<PagedResult<TopicDto>>(await business.GetAsync($"/api/v1/topics?departmentId={s.OtherDepartment}&projectMode=INTERDISCIPLINARY&search=HYBRID"));
        Assert.Equal(hybrid.Id, Assert.Single(page.Items).Id);
        page = await Body<PagedResult<TopicDto>>(await business.GetAsync($"/api/v1/topics?majorId={s.Major}&pageSize=1&page=2"));
        Assert.Equal(2, page.TotalCount); Assert.Single(page.Items);
        Assert.False((await Body<TopicDto>(await business.GetAsync($"/api/v1/topics/{single.Id}"))).MatchesMyMajor);
        Assert.Equal(HttpStatusCode.NotFound, (await business.GetAsync($"/api/v1/topics/{hidden.Id}")).StatusCode);
        Assert.Empty((await Body<PagedResult<TopicDto>>(await business.GetAsync($"/api/v1/topics?projectPeriodId={foreign.Period}"))).Items);
        Assert.Empty((await Body<PagedResult<TopicDto>>(await business.GetAsync("/api/v1/topics?status=DRAFT"))).Items);
    }

    [Fact]
    public async Task Publication_requires_current_scope_complete_content_and_database_policy()
    {
        var s = await database.Seed(); using var app = new Factory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff, roles: ["DEPARTMENT_STAFF"]);
        var input = Input(s) with { Content = Content(s) with { ProblemStatement = null, Keywords = [] } };
        var topic = await Create(staff, input);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync($"/api/v1/topics/{topic.Id}/publish", new PublishTopicRequest(topic.ConcurrencyToken))).StatusCode);
        topic = await Body<TopicDto>(await staff.PutAsJsonAsync($"/api/v1/topics/{topic.Id}", new UpdateTopicRequest(topic.ConcurrencyToken, Content(s))));
        await using (var db = database.CreateContext())
        {
            var period = (await db.ProjectPeriods.FindAsync(s.Period))!;
            period.MinTeamSize = null; period.MaxTeamSize = null; await db.SaveChangesAsync();
        }
        var noPolicy = await staff.PostAsJsonAsync($"/api/v1/topics/{topic.Id}/publish", new PublishTopicRequest(topic.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.Conflict, noPolicy.StatusCode);
        Assert.Contains("TEAM_POLICY_UNCONFIGURED", await noPolicy.Content.ReadAsStringAsync());
        await using (var db = database.CreateContext())
        {
            var period = (await db.ProjectPeriods.FindAsync(s.Period))!;
            period.MinTeamSize = 2; period.MaxTeamSize = 3; await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync($"/api/v1/topics/{topic.Id}/publish", new PublishTopicRequest(topic.ConcurrencyToken))).StatusCode);
        topic = await Body<TopicDto>(await staff.PutAsJsonAsync($"/api/v1/topics/{topic.Id}", new UpdateTopicRequest(topic.ConcurrencyToken,
            Content(s) with { Requirements = [new(s.Major, 2, 3, "Software")] })));
        await using (var db = database.CreateContext())
            await db.Majors.Where(m => m.Id == s.Major).ExecuteUpdateAsync(m => m.SetProperty(x => x.IsActive, false));
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync($"/api/v1/topics/{topic.Id}/publish", new PublishTopicRequest(topic.ConcurrencyToken))).StatusCode);
    }

    [Fact]
    public async Task Future_registration_can_publish_but_ended_period_cannot_and_close_remains_available()
    {
        var s = await database.Seed(); using var app = new Factory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff, roles: ["DEPARTMENT_STAFF"]);
        await using (var db = database.CreateContext())
            await db.ProjectPeriods.Where(p => p.Id == s.Period).ExecuteUpdateAsync(p => p.SetProperty(x => x.StartAt, TopicDatabaseFixture.Now.AddDays(1)));
        var topic = await Publish(staff, await Create(staff, Input(s)));
        await using (var db = database.CreateContext())
            await db.ProjectPeriods.Where(p => p.Id == s.Period).ExecuteUpdateAsync(p => p.SetProperty(x => x.EndAt, TopicDatabaseFixture.Now).SetProperty(x => x.StartAt, TopicDatabaseFixture.Now.AddDays(-1)));
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync("/api/v1/topics", Input(s, code: "ENDED"))).StatusCode);
        var closed = await Post(staff, $"/api/v1/topics/{topic.Id}/close", new CloseTopicRequest(topic.ConcurrencyToken, "Period ended"));
        Assert.Equal("CLOSED", closed.Status);
    }

    [Fact]
    public async Task Scope_rejects_foreign_periods_majors_wrong_lead_and_invalid_mode_requirements()
    {
        var s = await database.Seed(); var foreign = await database.Seed(); using var app = new Factory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff, roles: ["DEPARTMENT_STAFF"]);
        var inputs = new[] {
            Input(s) with { ProjectPeriodId = foreign.Period },
            Input(s) with { Content = Content(s) with { PrimaryMajorId = foreign.Major, Requirements = [new(foreign.Major, 2, 5, "Foreign")] } },
            Input(s) with { Content = Content(s) with { PrimaryMajorId = s.OtherMajor, Requirements = [new(s.OtherMajor, 2, 5, "Wrong lead")] } },
            Input(s) with { Content = Content(s, true) with { PrimaryMajorId = s.Major } },
            Input(s) with { Content = Content(s) with { Requirements = [new(s.Major, 1, 3, "A"), new(s.Major, 1, 3, "B")] } }
        };
        foreach (var input in inputs) Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync("/api/v1/topics", input)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync("/api/v1/topics", Input(s) with { LeadDepartmentId = s.OtherDepartment })).StatusCode);
    }

    [Fact]
    public async Task Stale_roles_and_platform_admin_do_not_acquire_academic_publication_authority()
    {
        var s = await database.Seed(); using var app = new Factory(database);
        using var lecturer = app.CreateAuthenticatedClient(s.Users.Lecturer, roles: ["LECTURER"]);
        using var stale = app.CreateAuthenticatedClient(s.Users.Lecturer, roles: ["DEPARTMENT_STAFF"]);
        using var admin = app.CreateAuthenticatedClient(s.Users.Admin, roles: ["ADMIN"]);
        using var student = app.CreateAuthenticatedClient(s.Users.Student, roles: ["STUDENT"]);
        var topic = await Create(lecturer, Input(s));
        Assert.Equal(HttpStatusCode.Forbidden, (await stale.PostAsJsonAsync("/api/v1/topics", Input(s, code: "STALE"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.PostAsJsonAsync("/api/v1/topics", Input(s, code: "STUDENT"))).StatusCode);
        Assert.Equal(topic.Id, (await Body<TopicDto>(await admin.GetAsync($"/api/v1/topics/{topic.Id}"))).Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync($"/api/v1/topics/{topic.Id}/publish", new PublishTopicRequest(topic.ConcurrencyToken))).StatusCode);
        await using (var db = database.CreateContext())
            await db.Users.Where(u => u.Id == s.Users.Lecturer).ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, "INACTIVE"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await lecturer.GetAsync("/api/v1/topics")).StatusCode);
    }

    [Theory]
    [InlineData("TOPIC_CREATED")]
    [InlineData("TOPIC_UPDATED")]
    [InlineData("TOPIC_PUBLISHED")]
    [InlineData("TOPIC_CLOSED")]
    public async Task Audit_failure_rolls_back_every_mutation_and_allows_retry(string action)
    {
        var s = await database.Seed(); using var app = new Factory(database);
        using var client = app.CreateAuthenticatedClient(s.Users.Staff, roles: ["DEPARTMENT_STAFF"]);
        TopicDto? topic = action == "TOPIC_CREATED" ? null : await Create(client, Input(s));
        using var failing = new Factory(database, action);
        using var failClient = failing.CreateAuthenticatedClient(s.Users.Staff, roles: ["DEPARTMENT_STAFF"]);
        Task<HttpResponseMessage> Send(HttpClient c) => action switch
        {
            "TOPIC_CREATED" => c.PostAsJsonAsync("/api/v1/topics", Input(s)),
            "TOPIC_UPDATED" => c.PutAsJsonAsync($"/api/v1/topics/{topic!.Id}", new UpdateTopicRequest(topic.ConcurrencyToken, Content(s, true))),
            "TOPIC_PUBLISHED" => c.PostAsJsonAsync($"/api/v1/topics/{topic!.Id}/publish", new PublishTopicRequest(topic.ConcurrencyToken)),
            _ => c.PostAsJsonAsync($"/api/v1/topics/{topic!.Id}/close", new CloseTopicRequest(topic.ConcurrencyToken, "Withdraw"))
        };
        Assert.Equal(HttpStatusCode.InternalServerError, (await Send(failClient)).StatusCode);
        await using (var db = database.CreateContext())
        {
            var rows = await db.Set<ProjectTopic>().AsNoTracking().Where(t => t.ProjectPeriodId == s.Period).ToArrayAsync();
            if (topic is null) Assert.Empty(rows);
            else
            {
                var row = Assert.Single(rows); Assert.Equal(topic.Status, row.Status); Assert.Equal(topic.ConcurrencyToken, row.ConcurrencyToken);
                Assert.Single(await db.Set<TopicMajorRequirement>().Where(r => r.TopicId == topic.Id).ToArrayAsync());
            }
            Assert.False(await db.AuditLogs.AnyAsync(a => a.Action == action && a.ActorUserId == s.Users.Staff));
        }
        Assert.True((await Send(client)).IsSuccessStatusCode);
    }

    [Fact]
    public async Task Duplicate_creation_and_concurrent_publications_produce_conflict_without_partial_audit()
    {
        var s = await database.Seed(); using var app = new Factory(database);
        using var a = app.CreateAuthenticatedClient(s.Users.Staff, roles: ["DEPARTMENT_STAFF"]);
        using var b = app.CreateAuthenticatedClient(s.Users.Staff, roles: ["DEPARTMENT_STAFF"]);
        var responses = await Task.WhenAll(a.PostAsJsonAsync("/api/v1/topics", Input(s)), b.PostAsJsonAsync("/api/v1/topics", Input(s, code: "topic")));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        var topic = await Body<TopicDto>(responses.Single(r => r.IsSuccessStatusCode));
        responses = await Task.WhenAll(a.PostAsJsonAsync($"/api/v1/topics/{topic.Id}/publish", new PublishTopicRequest(topic.ConcurrencyToken)),
            b.PostAsJsonAsync($"/api/v1/topics/{topic.Id}/publish", new PublishTopicRequest(topic.ConcurrencyToken)));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        await using var db = database.CreateContext();
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == "TOPIC_PUBLISHED" && a.EntityId == topic.Id.ToString()));
    }

    [Fact]
    public async Task Update_paused_before_lock_cannot_overwrite_concurrent_publication()
    {
        var s = await database.Seed(); using var app = new Factory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff, roles: ["DEPARTMENT_STAFF"]);
        var topic = await Create(staff, Input(s)); var gate = new TopicGate();
        using var slowApp = new Factory(database, gate: gate);
        using var slow = slowApp.CreateAuthenticatedClient(s.Users.Lecturer, roles: ["LECTURER"]);
        // Staff authored this topic, so use staff for the in-flight edit as well.
        using var editor = slowApp.CreateAuthenticatedClient(s.Users.Staff, roles: ["DEPARTMENT_STAFF"]);
        var pending = editor.PutAsJsonAsync($"/api/v1/topics/{topic.Id}", new UpdateTopicRequest(topic.ConcurrencyToken, Content(s) with { Title = "Late overwrite" }));
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            topic = await Publish(staff, topic);
        }
        finally { gate.Release.TrySetResult(); }
        Assert.Equal(HttpStatusCode.Conflict, (await pending).StatusCode);
        Assert.Equal("Capstone catalogue", (await Body<TopicDto>(await staff.GetAsync($"/api/v1/topics/{topic.Id}"))).Title);
    }

    [Fact]
    public async Task Migration_is_rerunnable_without_changing_catalogue_content()
    {
        var s = await database.Seed(); using var app = new Factory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff, roles: ["DEPARTMENT_STAFF"]);
        var topic = await Publish(staff, await Create(staff, Input(s, true)));
        await database.Migrate(); await database.Migrate();
        var after = await Body<TopicDto>(await staff.GetAsync($"/api/v1/topics/{topic.Id}"));
        Assert.Equal(topic.ConcurrencyToken, after.ConcurrencyToken);
        Assert.Equal(topic.PublishedAt, after.PublishedAt); Assert.Equal(2, after.Requirements.Count);
    }

    [Theory]
    [InlineData("/api/v1/topics/0")]
    [InlineData("/api/v1/topics?pageSize=101")]
    [InlineData("/api/v1/topics?projectMode=UNKNOWN")]
    [InlineData("/api/v1/topics?status=ACTIVE")]
    public async Task Authentication_and_query_validation(string url)
    {
        var s = await database.Seed(); using var app = new Factory(database);
        using var anonymous = app.CreateClient(); using var client = app.CreateAuthenticatedClient(s.Users.Student);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(url)).StatusCode);
    }

    private sealed class Factory(TopicDatabaseFixture database, string? failAuditAction = null, TopicGate? gate = null) : AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> {
                ["ConnectionStrings:DefaultConnection"] = database.ConnectionString }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(new FixedClock());
                if (gate is not null)
                {
                    services.RemoveAll<DbContextOptions<AipmsDbContext>>();
                    services.AddDbContext<AipmsDbContext>(o => o.UseSqlServer(database.ConnectionString).AddInterceptors(gate));
                }
                if (failAuditAction is not null)
                {
                    services.RemoveAll<IAuditTrail>();
                    services.AddScoped<IAuditTrail>(sp => new FailingAudit(ActivatorUtilities.CreateInstance<DatabaseAuditTrail>(sp), failAuditAction));
                }
            });
        }
    }
    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(TopicDatabaseFixture.Now); }
    private sealed class FailingAudit(IAuditTrail inner, string action) : IAuditTrail
    {
        public async Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            await inner.RecordAsync(entry, cancellationToken);
            if (entry.Action == action) throw new InvalidOperationException("Injected failure after audit persistence");
        }
    }
    private sealed class TopicGate : DbCommandInterceptor
    {
        private int armed = 1;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("dbo.project_topics WITH (UPDLOCK") && Interlocked.Exchange(ref armed, 0) == 1)
            { Entered.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), ct); }
            return result;
        }
    }
}
