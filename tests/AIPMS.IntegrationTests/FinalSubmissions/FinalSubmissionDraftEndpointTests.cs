using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.FinalSubmissions.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AIPMS.IntegrationTests.FinalSubmissions;

public sealed class FinalSubmissionDraftEndpointTests(FinalSubmissionDraftDatabaseFixture database)
    : IClassFixture<FinalSubmissionDraftDatabaseFixture>
{
    private static DateTime Now => FinalSubmissionDraftDatabaseFixture.Now;
    private static string Route(long projectId) => $"/api/v1/projects/{projectId}/final-submission-draft";
    private static CreateFinalSubmissionDraftRequest Input(FinalDraftScenario s, params long[] versions) => new(s.PeriodId, " Notes ", versions);
    private static UpdateFinalSubmissionDraftRequest Update(FinalDraftScenario s, FinalSubmissionDraftDto draft, params long[] versions) =>
        new(s.PeriodId, " Revised ", versions, draft.ConcurrencyToken);
    private static async Task<T> Body<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    [Fact]
    public async Task Lifecycle_selects_exact_BE08_version_and_reuses_private_download()
    {
        var s = await database.Seed();
        using var app = new Factory(database);
        using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        using var member = app.CreateAuthenticatedClient(s.MemberId);
        var draft = await Body<FinalSubmissionDraftDto>(await leader.PostAsJsonAsync(Route(s.ProjectId), Input(s)));
        Assert.Equal("Notes", draft.Notes);
        Assert.Empty(draft.Items);
        Assert.Equal("DRAFT", draft.Status);
        Assert.False(draft.IsLocked);
        Assert.True(draft.CanEdit);
        var deliverable = await Body<DeliverableDto>(await leader.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/deliverables",
            new SaveDeliverableRequest(null, "Final report", null, "REPORT", Now.AddHours(2))));
        async Task<DeliverableVersionDto> Upload(int expected, string text)
        {
            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
            file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            form.Add(file, "file", "report.txt");
            form.Add(new StringContent(expected.ToString()), "expectedLatestVersion");
            return await Body<DeliverableVersionDto>(await leader.PostAsync($"/api/v1/deliverables/{deliverable.Id}/versions", form));
        }
        var first = await Upload(0, "Version one");
        draft = await Body<FinalSubmissionDraftDto>(await leader.PutAsJsonAsync(Route(s.ProjectId), Update(s, draft, first.Id)));
        await Upload(1, "Version two");
        var readResponse = await member.GetAsync(Route(s.ProjectId));
        var read = await Body<FinalSubmissionDraftDto>(readResponse);
        Assert.False(read.CanEdit);
        Assert.Contains("LEADER_REQUIRED", read.EditBlockers);
        var selection = Assert.Single(read.Items);
        Assert.Equal(first.Id, selection.DeliverableVersionId);
        Assert.Equal(1, selection.VersionNumber);
        var selectedFile = Assert.Single(selection.Files);
        Assert.Equal("Version one", await (await member.GetAsync($"/api/v1/files/{selectedFile.Id}/download")).Content.ReadAsStringAsync());
        Assert.Contains("no-store", readResponse.Headers.CacheControl!.ToString());
        var json = await readResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("storagePath", json);
        Assert.DoesNotContain("storageKey", json);
        Assert.EndsWith("Z", read.CreatedAt.ToString("O"));
        draft = await Body<FinalSubmissionDraftDto>(await leader.PutAsJsonAsync(Route(s.ProjectId), Update(s, draft)));
        Assert.Empty(draft.Items);
        await using var db = database.CreateContext();
        Assert.Equal("ACTIVE", (await db.Projects.FindAsync(s.ProjectId))!.Status);
        Assert.Equal(3, await db.AuditLogs.CountAsync(a => a.EntityType == "FINAL_SUBMISSION_DRAFT" && a.EntityId == draft.Id.ToString()));
        Assert.Equal(2, await db.DeliverableVersions.CountAsync(v => v.DeliverableId == deliverable.Id));
    }

    [Fact]
    public async Task Team_can_discover_own_final_windows_before_creating_draft()
    {
        var s = await database.Seed();
        var foreign = await database.Seed();
        using var app = new Factory(database);
        using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        using var member = app.CreateAuthenticatedClient(s.MemberId);
        using var outsider = app.CreateAuthenticatedClient(foreign.Users.Student);
        var route = $"/api/v1/projects/{s.ProjectId}/final-submission-periods";
        var periods = await Body<PagedResult<FinalSubmissionPeriodOptionDto>>(await leader.GetAsync(route + "?pageSize=1"));
        Assert.Equal(1, periods.TotalCount);
        var option = Assert.Single(periods.Items);
        Assert.Equal(s.PeriodId, option.Id);
        Assert.True(option.CanPrepareDraft);
        Assert.Equal(Now.AddHours(1), option.EndAt);
        Assert.Empty((await Body<PagedResult<FinalSubmissionPeriodOptionDto>>(await leader.GetAsync(route + "?pageSize=1&page=2"))).Items);
        Assert.Equal(HttpStatusCode.BadRequest, (await leader.GetAsync(route + "?page=2147483647")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(route)).StatusCode);
        var memberOption = Assert.Single((await Body<PagedResult<FinalSubmissionPeriodOptionDto>>(await member.GetAsync(route))).Items);
        Assert.False(memberOption.CanPrepareDraft);
        Assert.Contains("LEADER_REQUIRED", memberOption.Blockers);
        Assert.Equal(HttpStatusCode.NotFound, (await leader.GetAsync(Route(s.ProjectId))).StatusCode);
    }

    [Theory]
    [InlineData("member")]
    [InlineData("staff")]
    [InlineData("admin")]
    [InlineData("lecturer")]
    [InlineData("outsider")]
    public async Task Read_access_and_claims_do_not_grant_leader_writes(string role)
    {
        var s = await database.Seed();
        using var app = new Factory(database);
        using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        var draft = await Body<FinalSubmissionDraftDto>(await leader.PostAsJsonAsync(Route(s.ProjectId), Input(s)));
        var id = role switch { "member" => s.MemberId, "staff" => s.Users.Staff, "admin" => s.Users.Admin,
            "lecturer" => s.Users.Lecturer, _ => s.Users.OtherLecturer };
        using var client = app.CreateAuthenticatedClient(id, roles: [AppRoles.Admin, AppRoles.Student]);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Route(s.ProjectId), Input(s))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(Route(s.ProjectId), Update(s, draft))).StatusCode);
        Assert.Equal(role == "member" ? HttpStatusCode.OK : HttpStatusCode.Forbidden, (await client.GetAsync(Route(s.ProjectId))).StatusCode);
    }

    [Fact]
    public async Task Authentication_missing_draft_and_invalid_input_return_ProblemDetails()
    {
        var s = await database.Seed();
        using var app = new Factory(database);
        using var anonymous = app.CreateClient();
        using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Route(s.ProjectId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await leader.GetAsync(Route(s.ProjectId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await leader.PutAsJsonAsync(Route(s.ProjectId), new UpdateFinalSubmissionDraftRequest(s.PeriodId, null, [], Guid.NewGuid().ToString()))).StatusCode);
        foreach (var input in new[] { new CreateFinalSubmissionDraftRequest(0, null, []), new(s.PeriodId, null, null!), new(s.PeriodId, null, [1, 1]), new(s.PeriodId, null, [-1]) })
        {
            var response = await leader.PostAsJsonAsync(Route(s.ProjectId), input);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        }
    }

    [Theory]
    [InlineData("FINAL_SUBMISSION")]
    [InlineData("COMPLETED")]
    [InlineData("ARCHIVED")]
    [InlineData("DRAFT")]
    public async Task Nonactive_project_blocks_writes_but_team_can_read_existing_draft(string status)
    {
        var s = await database.Seed();
        using var app = new Factory(database);
        using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        var draft = await Body<FinalSubmissionDraftDto>(await leader.PostAsJsonAsync(Route(s.ProjectId), Input(s)));
        await using (var db = database.CreateContext())
        {
            (await db.Projects.FindAsync(s.ProjectId))!.Status = status;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PutAsJsonAsync(Route(s.ProjectId), Update(s, draft))).StatusCode);
        var read = await Body<FinalSubmissionDraftDto>(await leader.GetAsync(Route(s.ProjectId)));
        Assert.False(read.CanEdit);
        Assert.Contains("PROJECT_NOT_ACTIVE", read.EditBlockers);
        Assert.Equal(draft.ConcurrencyToken, read.ConcurrencyToken);
    }

    [Theory]
    [InlineData(-3601, false)]
    [InlineData(-3600, true)]
    [InlineData(3599, true)]
    [InlineData(3600, false)]
    public async Task Final_window_start_inclusive_end_exclusive(int seconds, bool accepted)
    {
        var s = await database.Seed();
        using var app = new Factory(database, clock: new DraftClock(Now.AddSeconds(seconds)));
        using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        Assert.Equal(accepted ? HttpStatusCode.Created : HttpStatusCode.Conflict,
            (await leader.PostAsJsonAsync(Route(s.ProjectId), Input(s))).StatusCode);
    }

    [Theory]
    [InlineData("wrong-semester")]
    [InlineData("wrong-type")]
    [InlineData("inactive")]
    [InlineData("overlap")]
    [InlineData("semester-closed")]
    [InlineData("semester-dates")]
    [InlineData("major-inactive")]
    public async Task Invalid_academic_configuration_blocks_draft(string condition)
    {
        var s = await database.Seed();
        var periodId = s.PeriodId;
        if (condition == "wrong-semester") periodId = (await database.Seed()).PeriodId;
        await using (var db = database.CreateContext())
        {
            var period = (await db.ProjectPeriods.FindAsync(s.PeriodId))!;
            if (condition == "wrong-type") period.PeriodType = "EVALUATION";
            if (condition == "inactive") period.Status = "CLOSED";
            if (condition == "semester-closed") (await db.AcademicSemesters.FindAsync(s.SemesterId))!.Status = "CLOSED";
            if (condition == "semester-dates") (await db.AcademicSemesters.FindAsync(s.SemesterId))!.EndDate = DateOnly.FromDateTime(Now.AddDays(-1));
            if (condition == "major-inactive") (await db.ProjectMajors.Include(m => m.Major).SingleAsync(m => m.ProjectId == s.ProjectId)).Major.IsActive = false;
            if (condition == "overlap") db.ProjectPeriods.Add(new() { AcademicSemesterId = s.SemesterId, Code = "OVERLAP", Name = "Overlap",
                PeriodType = "FINAL_SUBMISSION", Status = "ACTIVE", StartAt = Now.AddMinutes(-5), EndAt = Now.AddMinutes(5) });
            await db.SaveChangesAsync();
        }
        using var app = new Factory(database);
        using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync(Route(s.ProjectId), new CreateFinalSubmissionDraftRequest(periodId, null, []))).StatusCode);
    }

    [Fact]
    public async Task Read_after_deadline_and_rebind_to_new_valid_period()
    {
        var s = await database.Seed();
        var clock = new DraftClock(Now);
        using var app = new Factory(database, clock: clock);
        using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        var draft = await Body<FinalSubmissionDraftDto>(await leader.PostAsJsonAsync(Route(s.ProjectId), Input(s)));
        clock.Now = Now.AddHours(1);
        var read = await Body<FinalSubmissionDraftDto>(await leader.GetAsync(Route(s.ProjectId)));
        Assert.Contains("WINDOW_CLOSED", read.EditBlockers);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PutAsJsonAsync(Route(s.ProjectId), Update(s, draft))).StatusCode);
        long nextPeriod;
        await using (var db = database.CreateContext())
        {
            var p = new Infrastructure.Persistence.Generated.Models.ProjectPeriod { AcademicSemesterId = s.SemesterId,
                Code = "SECOND", Name = "Second window", PeriodType = "FINAL_SUBMISSION", Status = "ACTIVE",
                StartAt = Now.AddHours(1), EndAt = Now.AddHours(2) };
            db.ProjectPeriods.Add(p);
            await db.SaveChangesAsync();
            nextPeriod = p.Id;
        }
        var rebound = await Body<FinalSubmissionDraftDto>(await leader.PutAsJsonAsync(Route(s.ProjectId),
            new UpdateFinalSubmissionDraftRequest(nextPeriod, null, [], draft.ConcurrencyToken)));
        Assert.Equal(nextPeriod, rebound.ProjectPeriodId);
        Assert.Equal(draft.Id, rebound.Id);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("missing")]
    [InlineData("rejected")]
    [InlineData("no-file")]
    [InlineData("checksum")]
    [InlineData("two-versions")]
    public async Task Invalid_version_selection_is_atomic(string condition)
    {
        var s = await database.Seed();
        var version = condition == "missing" ? long.MaxValue : await database.Version(condition == "foreign" ? await database.Seed() : s,
            status: condition == "rejected" ? "REJECTED" : "SUBMITTED", file: condition != "no-file");
        var ids = new List<long> { version };
        if (condition == "checksum")
        {
            await using var db = database.CreateContext();
            (await db.Files.SingleAsync(f => f.DeliverableVersionId == version)).ChecksumSha256 = null;
            await db.SaveChangesAsync();
        }
        if (condition == "two-versions")
        {
            await using var db = database.CreateContext();
            var deliverableId = (await db.DeliverableVersions.FindAsync(version))!.DeliverableId;
            ids.Add(await database.Version(s, deliverableId: deliverableId));
        }
        using var app = new Factory(database);
        using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        var draft = await Body<FinalSubmissionDraftDto>(await leader.PostAsJsonAsync(Route(s.ProjectId), Input(s)));
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PutAsJsonAsync(Route(s.ProjectId), Update(s, draft, ids.ToArray()))).StatusCode);
        var unchanged = await Body<FinalSubmissionDraftDto>(await leader.GetAsync(Route(s.ProjectId)));
        Assert.Empty(unchanged.Items);
        Assert.Equal(draft.ConcurrencyToken, unchanged.ConcurrencyToken);
    }

    [Fact]
    public async Task Review_change_is_visible_and_invalid_selection_can_be_removed()
    {
        var s = await database.Seed();
        var version = await database.Version(s);
        using var app = new Factory(database);
        using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        var draft = await Body<FinalSubmissionDraftDto>(await leader.PostAsJsonAsync(Route(s.ProjectId), Input(s, version)));
        await using (var db = database.CreateContext())
        {
            (await db.DeliverableVersions.FindAsync(version))!.Status = "REJECTED";
            await db.SaveChangesAsync();
        }
        var read = await Body<FinalSubmissionDraftDto>(await leader.GetAsync(Route(s.ProjectId)));
        Assert.False(Assert.Single(read.Items).IsEligible);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PutAsJsonAsync(Route(s.ProjectId), Update(s, draft, version))).StatusCode);
        Assert.Empty((await Body<FinalSubmissionDraftDto>(await leader.PutAsJsonAsync(Route(s.ProjectId), Update(s, draft)))).Items);
    }

    [Fact]
    public async Task Current_leader_and_membership_are_resolved_from_database()
    {
        var s = await database.Seed();
        using var app = new Factory(database);
        using var oldLeader = app.CreateAuthenticatedClient(s.Users.Student, roles: AppRoles.Admin);
        using var newLeader = app.CreateAuthenticatedClient(s.MemberId);
        var draft = await Body<FinalSubmissionDraftDto>(await oldLeader.PostAsJsonAsync(Route(s.ProjectId), Input(s)));
        await using (var db = database.CreateContext())
        {
            var former = await db.TeamMembers.SingleAsync(m => m.TeamId == s.TeamId && m.UserId == s.Users.Student);
            former.IsLeader = false;
            await db.SaveChangesAsync();
            (await db.TeamMembers.SingleAsync(m => m.TeamId == s.TeamId && m.UserId == s.MemberId)).IsLeader = true;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await oldLeader.PutAsJsonAsync(Route(s.ProjectId), Update(s, draft))).StatusCode);
        draft = await Body<FinalSubmissionDraftDto>(await newLeader.PutAsJsonAsync(Route(s.ProjectId), Update(s, draft)));
        Assert.Equal(s.Users.Student, draft.CreatedBy);
        Assert.Equal(s.MemberId, draft.UpdatedBy);
        await using (var db = database.CreateContext())
        {
            (await db.TeamMembers.SingleAsync(m => m.TeamId == s.TeamId && m.UserId == s.Users.Student)).LeftAt = Now;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await oldLeader.GetAsync(Route(s.ProjectId))).StatusCode);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("role-removed")]
    [InlineData("department-inactive")]
    public async Task Ineligible_actor_loses_access_even_with_old_claims(string condition)
    {
        var s = await database.Seed();
        using var app = new Factory(database);
        using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        var draft = await Body<FinalSubmissionDraftDto>(await leader.PostAsJsonAsync(Route(s.ProjectId), Input(s)));
        await using (var db = database.CreateContext())
        {
            if (condition == "inactive") (await db.Users.FindAsync(s.Users.Student))!.Status = "INACTIVE";
            if (condition == "role-removed") db.UserRoles.RemoveRange(await db.UserRoles.Where(r => r.UserId == s.Users.Student).ToListAsync());
            if (condition == "department-inactive") (await db.Departments.FindAsync(s.Users.DepartmentId))!.IsActive = false;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await leader.GetAsync(Route(s.ProjectId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await leader.PutAsJsonAsync(Route(s.ProjectId), Update(s, draft))).StatusCode);
    }

    [Fact]
    public async Task Concurrent_create_and_stale_updates_return_409_with_one_audit_per_winner()
    {
        var s = await database.Seed();
        using var app = new Factory(database);
        using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        var create = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => leader.PostAsJsonAsync(Route(s.ProjectId), Input(s))));
        Assert.Single(create, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal(2, create.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var draft = await Body<FinalSubmissionDraftDto>(create.Single(r => r.StatusCode == HttpStatusCode.Created));
        var firstVersion = await database.Version(s);
        var secondVersion = await database.Version(s, "ACCEPTED");
        var updates = await Task.WhenAll(leader.PutAsJsonAsync(Route(s.ProjectId), Update(s, draft, firstVersion)),
            leader.PutAsJsonAsync(Route(s.ProjectId), Update(s, draft, secondVersion)));
        Assert.Single(updates, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(updates, r => r.StatusCode == HttpStatusCode.Conflict);
        var read = await Body<FinalSubmissionDraftDto>(await leader.GetAsync(Route(s.ProjectId)));
        Assert.Single(read.Items);
        Assert.NotEqual(draft.ConcurrencyToken, read.ConcurrencyToken);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PutAsJsonAsync(Route(s.ProjectId), Update(s, draft))).StatusCode);
        await using var db = database.CreateContext();
        Assert.Equal(2, await db.AuditLogs.CountAsync(a => a.EntityType == "FINAL_SUBMISSION_DRAFT" && a.EntityId == read.Id.ToString()));
    }

    [Fact]
    public async Task Audit_failure_rolls_back_draft_selection_and_token()
    {
        var s = await database.Seed();
        using var working = new Factory(database);
        using var leader = working.CreateAuthenticatedClient(s.Users.Student);
        using var broken = new Factory(database, failAudit: true);
        using var failing = broken.CreateAuthenticatedClient(s.Users.Student);
        Assert.Equal(HttpStatusCode.InternalServerError, (await failing.PostAsJsonAsync(Route(s.ProjectId), Input(s))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await leader.GetAsync(Route(s.ProjectId))).StatusCode);
        var version = await database.Version(s);
        var draft = await Body<FinalSubmissionDraftDto>(await leader.PostAsJsonAsync(Route(s.ProjectId), Input(s, version)));
        Assert.Equal(HttpStatusCode.InternalServerError, (await failing.PutAsJsonAsync(Route(s.ProjectId), Update(s, draft))).StatusCode);
        var read = await Body<FinalSubmissionDraftDto>(await leader.GetAsync(Route(s.ProjectId)));
        Assert.Equal(draft.ConcurrencyToken, read.ConcurrencyToken);
        Assert.Equal(version, Assert.Single(read.Items).DeliverableVersionId);
    }

    [Fact]
    public async Task Crossing_deadline_during_audit_rolls_back_everything()
    {
        var s = await database.Seed();
        var clock = new DraftClock(Now);
        using var app = new Factory(database, clock: clock, advanceDuringAudit: true);
        using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync(Route(s.ProjectId), Input(s))).StatusCode);
        await using var db = database.CreateContext();
        Assert.False(await db.Set<FinalSubmissionDraft>().AnyAsync(d => d.ProjectId == s.ProjectId));
        Assert.False(await db.AuditLogs.AnyAsync(a => a.ActorUserId == s.Users.Student && a.Action == "FINAL_SUBMISSION_DRAFT_CREATED"));
    }

    [Fact]
    public async Task Project_change_before_lock_is_revalidated_and_cannot_save()
    {
        var s = await database.Seed();
        var gate = new ProjectLockGate();
        using var app = new Factory(database, interceptor: gate);
        using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        var pending = leader.PostAsJsonAsync(Route(s.ProjectId), Input(s));
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await using var db = database.CreateContext();
            (await db.Projects.FindAsync(s.ProjectId))!.Status = "FINAL_SUBMISSION";
            await db.SaveChangesAsync();
        }
        finally { gate.Release.TrySetResult(); }
        Assert.Equal(HttpStatusCode.Conflict, (await pending).StatusCode);
        await using var verify = database.CreateContext();
        Assert.False(await verify.Set<FinalSubmissionDraft>().AnyAsync(d => d.ProjectId == s.ProjectId));
    }

    [Fact]
    public async Task Migration_is_rerunnable_and_preserves_existing_records()
    {
        var s = await database.Seed();
        var version = await database.Version(s);
        using var app = new Factory(database);
        using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        var draft = await Body<FinalSubmissionDraftDto>(await leader.PostAsJsonAsync(Route(s.ProjectId), Input(s, version)));
        await database.Migrate();
        await database.Migrate();
        var read = await Body<FinalSubmissionDraftDto>(await leader.GetAsync(Route(s.ProjectId)));
        Assert.Equal(draft.ConcurrencyToken, read.ConcurrencyToken);
        Assert.Equal(version, Assert.Single(read.Items).DeliverableVersionId);
        await using var db = database.CreateContext();
        Assert.Equal("SUBMITTED", (await db.DeliverableVersions.FindAsync(version))!.Status);
        Assert.Equal("ACTIVE", (await db.Projects.FindAsync(s.ProjectId))!.Status);
    }

    private sealed class DraftClock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
    private sealed class Factory(FinalSubmissionDraftDatabaseFixture database, DraftClock? clock = null,
        bool failAudit = false, bool advanceDuringAudit = false, DbCommandInterceptor? interceptor = null) : AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:DefaultConnection"] = database.ConnectionString }));
            builder.ConfigureServices(services =>
            {
                var time = clock ?? new DraftClock(Now);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(time);
                services.RemoveAll<IFileStorage>();
                services.AddSingleton<IFileStorage, MemoryStorage>();
                if (interceptor is not null)
                {
                    services.RemoveAll<DbContextOptions<AipmsDbContext>>();
                    services.AddDbContext<AipmsDbContext>(o => o.UseSqlServer(database.ConnectionString).AddInterceptors(interceptor));
                }
                if (failAudit || advanceDuringAudit)
                {
                    services.RemoveAll<IAuditTrail>();
                    services.AddScoped<IAuditTrail>(p => new ProbeAudit(p.GetRequiredService<AipmsDbContext>(), time, failAudit));
                }
            });
        }
    }
    private sealed class ProbeAudit(AipmsDbContext db, DraftClock clock, bool fail) : IAuditTrail
    {
        public async Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            if (fail) throw new InvalidOperationException("Injected audit failure.");
            db.AuditLogs.Add(new() { ActorUserId = entry.ActorUserId, Action = entry.Action, EntityType = entry.EntityType,
                EntityId = entry.EntityId?.ToString(), OccurredAt = Now, Outcome = "SUCCESS" });
            await db.SaveChangesAsync(cancellationToken);
            clock.Now = Now.AddHours(1);
        }
    }
    private sealed class MemoryStorage : IFileStorage
    {
        private readonly ConcurrentDictionary<string, byte[]> objects = new();
        public async Task WriteAsync(string key, Stream content, CancellationToken ct)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
            if (!objects.TryAdd(key, buffer.ToArray())) throw new IOException("Already exists");
        }
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream(objects[key], false));
        public Task DeleteAsync(string key, CancellationToken ct) { objects.TryRemove(key, out _); return Task.CompletedTask; }
    }
    private sealed class ProjectLockGate : DbCommandInterceptor
    {
        private int entered;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("XLOCK, HOLDLOCK") && Interlocked.Exchange(ref entered, 1) == 0)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            return result;
        }
    }
}
