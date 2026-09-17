using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Contributions.DTOs;
using AIPMS.Infrastructure.Persistence.Repositories;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.Data.SqlClient;
using AIPMS.IntegrationTests.FinalSubmissions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Contributions;

public sealed class ContributionEndpointTests(FinalSubmissionDraftDatabaseFixture database)
    : IClassFixture<FinalSubmissionDraftDatabaseFixture>
{
    private static string Route(long projectId) => $"/api/v1/projects/{projectId}/contributions";
    private static async Task<T> Body<T>(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {text}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private sealed class Factory(FinalSubmissionDraftDatabaseFixture db, bool failAudit = false) : AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:DefaultConnection"] = db.ConnectionString }));
            if (failAudit) builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAuditTrail>();
                services.AddScoped<IAuditTrail, FailingAudit>();
            });
        }
    }
    private sealed class FailingAudit : IAuditTrail
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken ct = default) => throw new IOException("Audit unavailable");
    }

    [Fact]
    public async Task BR110_summary_and_evidence_are_scoped_paged_and_assignment_alone_is_not_work()
    {
        var s = await database.Seed();
        var other = await database.Seed();
        await database.Version(s);
        await database.Version(other);
        await using (var db = database.CreateContext())
        {
            var now = FinalSubmissionDraftDatabaseFixture.Now;
            var milestone = new M.Milestone { ProjectId = s.ProjectId, Title = "Plan", Status = "PLANNED", CreatedBy = s.Users.Student };
            db.Tasks.Add(new M.Task { Milestone = milestone, Title = "Assigned but unstarted", Status = "TODO", CreatedBy = s.Users.Student,
                TaskAssignees = [new() { UserId = s.MemberId, AssignedBy = s.Users.Student, AssignedAt = now }] });
            await db.SaveChangesAsync();
        }
        using var app = new Factory(database);
        using var student = app.CreateAuthenticatedClient(s.Users.Student);
        var summary = await Body<ContributionSummaryDto>(await student.GetAsync(Route(s.ProjectId)));
        Assert.Equal(2, summary.TotalCount);
        Assert.Equal("INSUFFICIENT_DATA", summary.DataStatus);
        var member = summary.Members.Single(m => m.UserId == s.MemberId);
        Assert.Equal(1, member.AssignedTasks);
        Assert.Equal(0, member.ActivityScore);
        var leader = summary.Members.Single(m => m.UserId == s.Users.Student);
        Assert.Equal(1, leader.ActivityScore);
        Assert.Equal(1, leader.UploadedFiles);
        Assert.Equal(2, leader.EvidenceCount);
        var evidence = await Body<PagedResult<ContributionEvidenceDto>>(await student.GetAsync(Route(s.ProjectId) + $"/{s.Users.Student}/evidence?pageSize=1"));
        Assert.Equal(2, evidence.TotalCount);
        var next = await Body<PagedResult<ContributionEvidenceDto>>(await student.GetAsync(Route(s.ProjectId) + $"/{s.Users.Student}/evidence?pageSize=1&page=2"));
        Assert.NotEqual(Assert.Single(evidence.Items).SourceType, Assert.Single(next.Items).SourceType);
        Assert.All(evidence.Items.Concat(next.Items), e => Assert.Equal(DateTimeKind.Utc, e.OccurredAt.Kind));
        var page = await Body<ContributionSummaryDto>(await student.GetAsync(Route(s.ProjectId) + "?pageSize=1"));
        Assert.Single(page.Members);
        Assert.Equal(summary.ActivityVariance, page.ActivityVariance);
        var files = await Body<PagedResult<ContributionEvidenceDto>>(await student.GetAsync(Route(s.ProjectId) + $"/{s.Users.Student}/evidence?sourceType=FILE"));
        Assert.Equal(0, Assert.Single(files.Items).Credit);
        Assert.Equal(HttpStatusCode.NotFound, (await student.GetAsync(Route(s.ProjectId) + $"/{other.Users.Student}/evidence")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync(Route(other.ProjectId))).StatusCode);
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?page=2147483647")]
    [InlineData("?pageSize=101")]
    public async Task Invalid_pagination_returns_problem_details(string suffix)
    {
        var s = await database.Seed();
        using var app = new Factory(database);
        using var client = app.CreateAuthenticatedClient(s.Users.Student);
        var response = await client.GetAsync(Route(s.ProjectId) + suffix);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Read_and_rebuild_recheck_persisted_role_and_department_even_with_forged_claims()
    {
        var s = await database.Seed();
        using var app = new Factory(database);
        using var anonymous = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Route(s.ProjectId))).StatusCode);
        using var student = app.CreateAuthenticatedClient(s.Users.Student, roles: AppRoles.Admin);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.PostAsync(Route(s.ProjectId) + "/snapshot", null)).StatusCode);
        using var outside = app.CreateAuthenticatedClient(s.Users.OutsideStaff, roles: AppRoles.DepartmentStaff);
        Assert.Equal(HttpStatusCode.Forbidden, (await outside.GetAsync(Route(s.ProjectId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outside.PostAsync(Route(s.ProjectId) + "/snapshot", null)).StatusCode);
        await using (var db = database.CreateContext())
        {
            (await db.Users.FindAsync(s.Users.Student))!.Status = "INACTIVE";
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync(Route(s.ProjectId))).StatusCode);
    }

    [Fact]
    public async Task Concurrent_rebuilds_commit_one_generation_and_one_audit_with_actor()
    {
        var s = await database.Seed();
        await database.Version(s);
        using var app = new Factory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff, roles: AppRoles.DepartmentStaff);
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => staff.PostAsync(Route(s.ProjectId) + "/snapshot", null)));
        var results = await Task.WhenAll(responses.Select(Body<ContributionSummaryDto>));
        Assert.Single(results.Select(r => r.SnapshotHash).Distinct());
        Assert.Single(results.Select(r => r.SnapshotAt).Distinct());
        await using var db = database.CreateContext();
        Assert.Equal(2, await db.ContributionSnapshots.CountAsync(snap => snap.ProjectId == s.ProjectId));
        var audit = Assert.Single(await db.AuditLogs.Where(a => a.Action == "CONTRIBUTION_SNAPSHOT_REBUILT" && a.EntityId == s.ProjectId.ToString()).ToListAsync());
        Assert.Equal(s.Users.Staff, audit.ActorUserId);
        Assert.Contains(results[0].SnapshotHash!, audit.DetailsJson!);
    }

    [Fact]
    public async Task Audit_failure_rolls_back_all_snapshot_rows()
    {
        var s = await database.Seed();
        using var app = new Factory(database, failAudit: true);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff, roles: AppRoles.DepartmentStaff);
        Assert.Equal(HttpStatusCode.InternalServerError, (await staff.PostAsync(Route(s.ProjectId) + "/snapshot", null)).StatusCode);
        await using var db = database.CreateContext();
        Assert.False(await db.ContributionSnapshots.AnyAsync(snap => snap.ProjectId == s.ProjectId));
    }

    [Fact]
    public async Task Equal_counts_with_changed_evidence_create_new_snapshot_and_reverting_creates_new_generation()
    {
        var s = await database.Seed();
        var version = await database.Version(s);
        using var app = new Factory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff, roles: AppRoles.DepartmentStaff);
        async Task<ContributionSummaryDto> Rebuild() => await Body<ContributionSummaryDto>(await staff.PostAsync(Route(s.ProjectId) + "/snapshot", null));
        var first = await Rebuild();
        async Task Rename(string name)
        {
            await using var db = database.CreateContext();
            (await db.DeliverableVersions.Include(v => v.Deliverable).SingleAsync(v => v.Id == version)).Deliverable.Title = name;
            await db.SaveChangesAsync();
        }
        await Rename("Changed label");
        var second = await Rebuild();
        Assert.NotEqual(first.SnapshotHash, second.SnapshotHash);
        Assert.Equal(first.Members[0].ActivityScore, second.Members[0].ActivityScore);
        await Rename("Report");
        var third = await Rebuild();
        Assert.NotEqual(first.SnapshotHash, third.SnapshotHash);
        Assert.Equal(third.SnapshotHash, (await Rebuild()).SnapshotHash);
        var stored = await Body<ContributionSummaryDto>(await staff.GetAsync(Route(s.ProjectId) + "?snapshot=true"));
        Assert.Equal(third.SnapshotHash, stored.SnapshotHash);
        await using var db = database.CreateContext();
        Assert.Equal(6, await db.ContributionSnapshots.CountAsync(snap => snap.ProjectId == s.ProjectId));
    }

    [Fact]
    public async Task Archived_summary_and_evidence_are_frozen_and_rebuild_is_rejected()
    {
        var s = await database.Seed();
        var version = await database.Version(s);
        using var app = new Factory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff, roles: AppRoles.DepartmentStaff);
        var snapshot = await Body<ContributionSummaryDto>(await staff.PostAsync(Route(s.ProjectId) + "/snapshot", null));
        await using (var db = database.CreateContext())
        {
            (await db.Projects.FindAsync(s.ProjectId))!.Status = "ARCHIVED";
            (await db.DeliverableVersions.Include(v => v.Deliverable).SingleAsync(v => v.Id == version)).Deliverable.Title = "Changed after snapshot";
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsync(Route(s.ProjectId) + "/snapshot", null)).StatusCode);
        var stored = await Body<ContributionSummaryDto>(await staff.GetAsync(Route(s.ProjectId)));
        Assert.Equal(snapshot.SnapshotHash, stored.SnapshotHash);
        var evidence = await Body<PagedResult<ContributionEvidenceDto>>(await staff.GetAsync(Route(s.ProjectId) + $"/{s.Users.Student}/evidence?sourceType=DELIVERABLE_VERSION"));
        Assert.Equal("Report", Assert.Single(evidence.Items).Label);
        await using var verify = database.CreateContext();
        Assert.Equal(2, await verify.ContributionSnapshots.CountAsync(snap => snap.ProjectId == s.ProjectId));
    }

    [Fact]
    public async Task Rebuild_waiting_for_archive_lock_rechecks_status_after_commit()
    {
        var s = await database.Seed();
        using var app = new Factory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff, roles: AppRoles.DepartmentStaff);
        await using var blocker = new SqlConnection(database.ConnectionString);
        await blocker.OpenAsync();
        await using var transaction = (SqlTransaction)await blocker.BeginTransactionAsync();
        await using var archive = new SqlCommand("UPDATE dbo.projects SET status='ARCHIVED' WHERE id=@id; SELECT @@SPID", blocker, transaction);
        archive.Parameters.AddWithValue("@id", s.ProjectId);
        var session = Convert.ToInt32(await archive.ExecuteScalarAsync());
        var pending = staff.PostAsync(Route(s.ProjectId) + "/snapshot", null);
        await using var observer = new SqlConnection(database.ConnectionString);
        await observer.OpenAsync();
        await using var waiting = new SqlCommand("SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id=@session", observer);
        waiting.Parameters.AddWithValue("@session", session);
        var observed = false;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!timeout.IsCancellationRequested)
            {
                if (Convert.ToInt32(await waiting.ExecuteScalarAsync(timeout.Token)) > 0) { observed = true; break; }
                await Task.Delay(25, timeout.Token);
            }
        }
        finally { await transaction.CommitAsync(); }
        var response = await pending;
        Assert.True(observed, "Rebuild must wait on the real SQL project lock before archive commits.");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var verify = database.CreateContext();
        Assert.False(await verify.ContributionSnapshots.AnyAsync(x => x.ProjectId == s.ProjectId));
        Assert.False(await verify.AuditLogs.AnyAsync(x => x.EntityId == s.ProjectId.ToString() && x.Action == "CONTRIBUTION_SNAPSHOT_REBUILT"));
    }

    [Fact]
    public async Task Only_completed_project_activity_during_membership_receives_credit()
    {
        var s = await database.Seed();
        var now = FinalSubmissionDraftDatabaseFixture.Now;
        await using (var db = database.CreateContext())
        {
            var statuses = new[] { "DRAFT", "SUBMITTED", "REVIEWED", "SUBMITTED", "SUBMITTED" };
            for (var i = 0; i < statuses.Length; i++)
                db.ProgressReports.Add(new M.ProgressReport { ProjectId = s.ProjectId,
                    SubmittedBy = i == 3 ? s.Users.OutsideStaff : s.Users.Student, ReportType = "WEEKLY",
                    PeriodStart = DateOnly.FromDateTime(now.AddDays(i)), PeriodEnd = DateOnly.FromDateTime(now.AddDays(i + 1)),
                    Summary = "Evidence", Status = statuses[i], SubmittedAt = i == 4 ? now.AddDays(-2) : now });
            foreach (var status in new[] { "COMPLETED", "SCHEDULED", "CANCELLED" })
                db.Meetings.Add(new M.Meeting { ProjectId = s.ProjectId, Title = status, StartAt = now, Status = status,
                    CreatedBy = s.Users.Student, MeetingParticipants = [
                        new() { UserId = s.Users.Student, AttendanceStatus = "ATTENDED" },
                        new() { UserId = s.MemberId, AttendanceStatus = "ABSENT" },
                        new() { UserId = s.Users.OutsideStaff, AttendanceStatus = "ATTENDED" }] });
            await db.SaveChangesAsync();
        }
        using var app = new Factory(database);
        using var client = app.CreateAuthenticatedClient(s.Users.Student);
        var summary = await Body<ContributionSummaryDto>(await client.GetAsync(Route(s.ProjectId)));
        var leader = summary.Members.Single(m => m.UserId == s.Users.Student);
        Assert.Equal(2, leader.SubmittedReports);
        Assert.Equal(1, leader.AttendedMeetings);
        Assert.Equal(3, leader.ActivityScore);
        Assert.Equal(0, summary.Members.Single(m => m.UserId == s.MemberId).ActivityScore);
        Assert.Equal("SUFFICIENT", summary.DataStatus);
        Assert.Equal(2.25, summary.ActivityVariance);
        var evidence = await Body<PagedResult<ContributionEvidenceDto>>(await client.GetAsync(Route(s.ProjectId) + $"/{s.Users.Student}/evidence"));
        Assert.Equal(3, evidence.TotalCount);
        Assert.Equal(leader.ActivityScore, evidence.Items.Sum(e => e.Credit));
    }

    [Fact]
    public async Task Missing_snapshot_empty_team_and_invalid_evidence_have_explicit_errors()
    {
        var s = await database.Seed();
        using var app = new Factory(database);
        using var client = app.CreateAuthenticatedClient(s.Users.Staff, roles: AppRoles.DepartmentStaff);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Route(long.MaxValue))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Route(s.ProjectId) + "?snapshot=true")).StatusCode);
        foreach (var query in new[] { "?sourceType=PRIVATE_NOTES", "?pageSize=0", "?page=-1" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(Route(s.ProjectId) + $"/{s.MemberId}/evidence" + query)).StatusCode);
        var emptyPage = await Body<PagedResult<ContributionEvidenceDto>>(await client.GetAsync(Route(s.ProjectId) + $"/{s.MemberId}/evidence?page=1000000"));
        Assert.Empty(emptyPage.Items);
        await using (var db = database.CreateContext())
        {
            db.TeamMembers.RemoveRange(await db.TeamMembers.Where(m => m.TeamId == s.TeamId).ToListAsync());
            await db.SaveChangesAsync();
        }
        var empty = await Body<ContributionSummaryDto>(await client.GetAsync(Route(s.ProjectId)));
        Assert.Empty(empty.Members);
        Assert.Equal("INSUFFICIENT_DATA", empty.DataStatus);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync(Route(s.ProjectId) + "/snapshot", null)).StatusCode);
        await using (var db = database.CreateContext())
        {
            (await db.Projects.FindAsync(s.ProjectId))!.Status = "ARCHIVED";
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Route(s.ProjectId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Route(s.ProjectId) + $"/{s.MemberId}/evidence")).StatusCode);
    }

    [Fact]
    public async Task Legacy_archive_reads_saved_members_and_does_not_invent_frozen_evidence()
    {
        var s = await database.Seed();
        await using (var db = database.CreateContext())
        {
            db.ContributionSnapshots.Add(new ContributionSnapshot { ProjectId = s.ProjectId, UserId = s.Users.Student,
                SnapshotAt = FinalSubmissionDraftDatabaseFixture.Now, SnapshotHash = new string('A', 64),
                SnapshotJson = JsonSerializer.Serialize(new[] { new ContributionMemberDto(s.Users.Student, "Saved name", 0, 0, 0, 0, 0, 0, 0) }) });
            (await db.Projects.FindAsync(s.ProjectId))!.Status = "ARCHIVED";
            await db.SaveChangesAsync();
        }
        using var app = new Factory(database);
        using var client = app.CreateAuthenticatedClient(s.Users.Staff, roles: AppRoles.DepartmentStaff);
        var summary = await Body<ContributionSummaryDto>(await client.GetAsync(Route(s.ProjectId)));
        Assert.Equal("activity-v1", summary.RuleVersion);
        Assert.Equal("Saved name", Assert.Single(summary.Members).DisplayName);
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync(Route(s.ProjectId) + $"/{s.Users.Student}/evidence")).StatusCode);
    }

    [Fact]
    public async Task BR112_shared_completed_task_credit_is_fractional_and_departed_member_evidence_remains()
    {
        var s = await database.Seed();
        var now = FinalSubmissionDraftDatabaseFixture.Now;
        await using (var db = database.CreateContext())
        {
            var milestone = new M.Milestone { ProjectId = s.ProjectId, Title = "Plan", Status = "PLANNED", CreatedBy = s.Users.Student };
            db.Tasks.Add(new M.Task { Milestone = milestone, Title = "Shared work", Status = "DONE", CompletedAt = now, CreatedBy = s.Users.Student,
                TaskAssignees = [new() { UserId = s.Users.Student, AssignedBy = s.Users.Student, AssignedAt = now.AddHours(-1) },
                    new() { UserId = s.MemberId, AssignedBy = s.Users.Student, AssignedAt = now.AddHours(-1) }] });
            (await db.TeamMembers.SingleAsync(m => m.TeamId == s.TeamId && m.UserId == s.MemberId)).LeftAt = now.AddHours(1);
            await db.SaveChangesAsync();
        }
        using var app = new Factory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff, roles: AppRoles.DepartmentStaff);
        var summary = await Body<ContributionSummaryDto>(await staff.GetAsync(Route(s.ProjectId)));
        Assert.Equal(2, summary.Members.Count);
        Assert.All(summary.Members, m => Assert.Equal(.5, m.ActivityScore));
        var evidence = await Body<PagedResult<ContributionEvidenceDto>>(await staff.GetAsync(Route(s.ProjectId) + $"/{s.MemberId}/evidence"));
        Assert.Equal(.5, Assert.Single(evidence.Items).Credit);
    }
}
