using AIPMS.AI.Services;
using System.Net.Http.Json;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Notifications.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.Infrastructure.Persistence.Repositories;
using AIPMS.IntegrationTests.FinalSubmissions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Notifications;

public sealed class ScheduledNotificationTests(FinalSubmissionDraftDatabaseFixture fixture)
    : IClassFixture<FinalSubmissionDraftDatabaseFixture>
{
    private static readonly DateTime Now = FinalSubmissionDraftDatabaseFixture.Now;
    private static ScheduledNotificationService Service(AipmsDbContext db) =>
        new(db, new ProjectProgressDataReader(db), new RuleBasedProgressAnalysisService());

    private async Task Run(long projectId, DateTime? now = null)
    {
        await using var db = fixture.CreateContext();
        await Service(db).ProcessProjectAsync(projectId, now ?? Now, TimeSpan.FromHours(24), default);
    }

    private async Task<long> TaskRow(FinalDraftScenario s, DateTime? deadline, string status = "TODO")
    {
        await using var db = fixture.CreateContext();
        var task = new M.Task { Title = "Private task title", Status = status, DueAt = deadline,
            CreatedBy = s.Users.Student, Milestone = new M.Milestone { ProjectId = s.ProjectId,
                Title = "Milestone", Status = "IN_PROGRESS", CreatedBy = s.Users.Student } };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        return task.Id;
    }

    private async Task<List<M.Notification>> Notifications(long projectId)
    {
        await using var db = fixture.CreateContext();
        var rows = await db.Set<ScheduledNotificationOccurrence>().Where(o => o.ProjectId == projectId)
            .Include(o => o.Notification).ThenInclude(n => n.NotificationRecipients).ToListAsync();
        return rows.Select(o => o.Notification).ToList();
    }

    [Theory]
    [InlineData(86401, null)]
    [InlineData(86400, "TASK_DEADLINE_REMINDER")]
    [InlineData(1, "TASK_DEADLINE_REMINDER")]
    [InlineData(0, "TASK_DEADLINE_REMINDER")]
    [InlineData(-1, "TASK_OVERDUE")]
    public async Task Task_deadline_boundaries_match_BE06(int seconds, string? expected)
    {
        var s = await fixture.Seed();
        var id = await TaskRow(s, Now.AddSeconds(seconds));
        await Run(s.ProjectId);
        var rows = (await Notifications(s.ProjectId)).Where(n => n.RelatedEntityType == "TASK").ToArray();
        if (expected is null) Assert.Empty(rows);
        else
        {
            var row = Assert.Single(rows);
            Assert.Equal(expected, row.NotificationType);
            Assert.Equal(id, row.RelatedEntityId);
            Assert.Null(row.CreatedBy);
            Assert.DoesNotContain("Private task title", row.Content);
            Assert.Equal(new[] { s.Users.Student, s.MemberId }.Order(), row.NotificationRecipients.Select(r => r.UserId).Order());
        }
    }

    [Theory]
    [InlineData("DONE")]
    [InlineData("CANCELLED")]
    public async Task Finished_tasks_are_not_reminded(string status)
    {
        var s = await fixture.Seed();
        await TaskRow(s, Now.AddHours(-1), status);
        await TaskRow(s, null);
        await Run(s.ProjectId);
        Assert.DoesNotContain(await Notifications(s.ProjectId), n => n.RelatedEntityType == "TASK");
    }

    [Theory]
    [InlineData("COMPLETED")]
    [InlineData("CANCELLED")]
    public async Task Closed_milestones_suppress_task_reminders(string status)
    {
        var s = await fixture.Seed();
        var id = await TaskRow(s, Now);
        await using (var db = fixture.CreateContext())
            await db.Milestones.Where(m => m.Tasks.Any(t => t.Id == id)).ExecuteUpdateAsync(x => x.SetProperty(m => m.Status, status));
        await Run(s.ProjectId);
        Assert.DoesNotContain(await Notifications(s.ProjectId), n => n.RelatedEntityType == "TASK");
    }

    [Fact]
    public async Task Concurrent_sweeps_deduplicate_and_rescheduling_creates_a_new_occurrence()
    {
        var s = await fixture.Seed();
        var id = await TaskRow(s, Now.AddHours(2));
        await Task.WhenAll(Run(s.ProjectId), Run(s.ProjectId), Run(s.ProjectId));
        Assert.Single((await Notifications(s.ProjectId)).Where(n => n.NotificationType == "TASK_DEADLINE_REMINDER"));
        await using (var db = fixture.CreateContext())
            await db.Tasks.Where(t => t.Id == id).ExecuteUpdateAsync(x => x.SetProperty(t => t.DueAt, Now.AddHours(3)));
        await Run(s.ProjectId);
        await Run(s.ProjectId, Now.AddHours(4));
        await Run(s.ProjectId, Now.AddHours(5));
        var rows = await Notifications(s.ProjectId);
        Assert.Equal(2, rows.Count(n => n.NotificationType == "TASK_DEADLINE_REMINDER"));
        Assert.Single(rows.Where(n => n.NotificationType == "TASK_OVERDUE"));
        Assert.All(rows, n => Assert.Equal(n.NotificationRecipients.Count, n.NotificationRecipients.Select(r => r.UserId).Distinct().Count()));
    }

    [Theory]
    [InlineData("DRAFT", null, true)]
    [InlineData("OPEN", null, true)]
    [InlineData("REJECTED", "REJECTED", true)]
    [InlineData("SUBMITTED", null, false)]
    [InlineData("ACCEPTED", null, false)]
    [InlineData("CLOSED", null, false)]
    [InlineData("OPEN", "SUBMITTED", false)]
    [InlineData("OPEN", "ACCEPTED", false)]
    public async Task Deliverable_reminders_require_pending_work(string status, string? versionStatus, bool expected)
    {
        var s = await fixture.Seed();
        await using (var db = fixture.CreateContext())
        {
            var row = new M.Deliverable { ProjectId = s.ProjectId, Title = "Report", Status = status,
                DueAt = Now.AddHours(-1), CreatedBy = s.Users.Student };
            if (versionStatus is not null) row.DeliverableVersions.Add(new() { VersionNumber = 1,
                Status = versionStatus, SubmittedBy = s.Users.Student, SubmittedAt = Now });
            db.Deliverables.Add(row);
            await db.SaveChangesAsync();
        }
        await Run(s.ProjectId);
        Assert.Equal(expected ? 1 : 0, (await Notifications(s.ProjectId)).Count(n => n.NotificationType == "DELIVERABLE_OVERDUE"));
    }

    [Fact]
    public async Task Final_window_end_is_exclusive_and_links_to_project()
    {
        var s = await fixture.Seed();
        await Run(s.ProjectId);
        await Run(s.ProjectId, Now.AddHours(1));
        await Run(s.ProjectId, Now.AddHours(2));
        var rows = await Notifications(s.ProjectId);
        Assert.Single(rows.Where(n => n.NotificationType == "FINAL_SUBMISSION_DEADLINE_REMINDER"));
        var overdue = Assert.Single(rows.Where(n => n.NotificationType == "FINAL_SUBMISSION_OVERDUE"));
        Assert.Equal("PROJECT", overdue.RelatedEntityType);
        Assert.Equal(s.ProjectId, overdue.RelatedEntityId);
    }

    [Fact]
    public async Task Future_or_ambiguous_final_windows_are_not_guessed()
    {
        var s = await fixture.Seed();
        await Run(s.ProjectId, Now.AddHours(-2));
        Assert.Empty(await Notifications(s.ProjectId));
        await using (var db = fixture.CreateContext())
        {
            db.ProjectPeriods.Add(new() { AcademicSemesterId = s.SemesterId, Code = "OVERLAP", Name = "Overlap",
                PeriodType = "FINAL_SUBMISSION", Status = "ACTIVE", StartAt = Now.AddMinutes(-1), EndAt = Now.AddHours(2) });
            await db.SaveChangesAsync();
        }
        await Run(s.ProjectId);
        Assert.Empty(await Notifications(s.ProjectId));
    }

    [Theory]
    [InlineData("ARCHIVED")]
    [InlineData("COMPLETED")]
    [InlineData("FINAL_SUBMISSION")]
    public async Task Non_active_projects_are_rechecked_before_publication(string status)
    {
        var s = await fixture.Seed();
        await TaskRow(s, Now.AddDays(-1));
        await using (var db = fixture.CreateContext())
        {
            Assert.Contains(s.ProjectId, await Service(db).GetProjectIdsAsync(s.ProjectId - 1, 1, default));
            await db.Projects.Where(p => p.Id == s.ProjectId).ExecuteUpdateAsync(x => x.SetProperty(p => p.Status, status));
        }
        await Run(s.ProjectId);
        Assert.Empty(await Notifications(s.ProjectId));
    }

    [Fact]
    public async Task Locked_submission_suppresses_final_reminders_even_if_project_status_is_stale()
    {
        var s = await fixture.Seed();
        await using (var db = fixture.CreateContext())
        {
            db.Set<FinalSubmission>().Add(new() { ProjectId = s.ProjectId, ProjectPeriodId = s.PeriodId,
                SubmittedBy = s.Users.Student, SubmittedAt = Now, Deadline = Now.AddHours(1),
                DraftConcurrencyToken = Guid.NewGuid(), RequirementsConcurrencyToken = Guid.NewGuid() });
            await db.SaveChangesAsync();
        }
        await Run(s.ProjectId);
        Assert.Empty(await Notifications(s.ProjectId));
    }

    [Theory]
    [InlineData("left")]
    [InlineData("inactive")]
    [InlineData("role")]
    [InlineData("department")]
    [InlineData("organization")]
    public async Task Current_recipient_eligibility_is_required(string change)
    {
        var s = await fixture.Seed();
        await using (var db = fixture.CreateContext())
        {
            if (change == "left") await db.TeamMembers.Where(m => m.TeamId == s.TeamId && m.UserId == s.MemberId)
                .ExecuteUpdateAsync(x => x.SetProperty(m => m.LeftAt, Now));
            if (change == "inactive") await db.Users.Where(u => u.Id == s.MemberId).ExecuteUpdateAsync(x => x.SetProperty(u => u.Status, "INACTIVE"));
            if (change == "role") await db.UserRoles.Where(r => r.UserId == s.MemberId).ExecuteDeleteAsync();
            if (change == "department") await db.Departments.Where(d => d.Id == s.Users.DepartmentId).ExecuteUpdateAsync(x => x.SetProperty(d => d.IsActive, false));
            if (change == "organization") await db.Organizations.Where(o => o.Departments.Any(d => d.Id == s.Users.DepartmentId))
                .ExecuteUpdateAsync(x => x.SetProperty(o => o.IsActive, false));
        }
        await Run(s.ProjectId);
        Assert.DoesNotContain((await Notifications(s.ProjectId)).SelectMany(n => n.NotificationRecipients), r => r.UserId == s.MemberId);
    }

    [Fact]
    public async Task An_eligible_member_added_later_receives_the_existing_occurrence_once()
    {
        var s = await fixture.Seed();
        await using (var db = fixture.CreateContext())
            await db.TeamMembers.Where(m => m.TeamId == s.TeamId && m.UserId == s.MemberId).ExecuteUpdateAsync(x => x.SetProperty(m => m.LeftAt, Now));
        await Run(s.ProjectId);
        await using (var db = fixture.CreateContext())
            await db.TeamMembers.Where(m => m.TeamId == s.TeamId && m.UserId == s.MemberId).ExecuteUpdateAsync(x => x.SetProperty(m => m.LeftAt, (DateTime?)null));
        await Run(s.ProjectId);
        await Run(s.ProjectId);
        Assert.Equal(2, Assert.Single(await Notifications(s.ProjectId)).NotificationRecipients.Count);
    }

    [Fact]
    public async Task Risk_uses_BE06_and_is_deduplicated_per_UTC_day_and_level()
    {
        var s = await fixture.Seed();
        await TaskRow(s, Now.AddDays(-4), "BLOCKED");
        await using (var db = fixture.CreateContext())
        {
            var facts = await new ProjectProgressDataReader(db).GetProjectProgressFactsAsync(s.ProjectId, default);
            Assert.Equal("CRITICAL", new RuleBasedProgressAnalysisService().Analyze(facts!, Now).RiskLevel);
        }
        await Run(s.ProjectId);
        await Run(s.ProjectId, Now.AddMinutes(5));
        Assert.Single((await Notifications(s.ProjectId)).Where(n => n.NotificationType == "PROJECT_RISK_CRITICAL"));
        await Run(s.ProjectId, Now.AddDays(1));
        Assert.Equal(2, (await Notifications(s.ProjectId)).Count(n => n.NotificationType == "PROJECT_RISK_CRITICAL"));
    }

    [Fact]
    public async Task Insufficient_data_does_not_raise_risk_and_cancellation_writes_nothing()
    {
        var s = await fixture.Seed();
        await using (var db = fixture.CreateContext())
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(db).ProcessProjectAsync(
                s.ProjectId, Now, TimeSpan.FromHours(24), new CancellationToken(true)));
        Assert.Empty(await Notifications(s.ProjectId));
        await Run(s.ProjectId);
        Assert.DoesNotContain(await Notifications(s.ProjectId), n => n.NotificationType.StartsWith("PROJECT_RISK_"));
    }

    [Fact]
    public async Task Failure_after_SQL_save_rolls_back_inbox_and_occurrence_and_retry_succeeds()
    {
        var s = await fixture.Seed();
        await TaskRow(s, Now);
        var options = new DbContextOptionsBuilder<AipmsDbContext>().UseSqlServer(fixture.ConnectionString)
            .AddInterceptors(new FailAfterSave()).Options;
        await using (var db = new AipmsDbContext(options))
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).ProcessProjectAsync(s.ProjectId, Now, TimeSpan.FromHours(24), default));
        Assert.Empty(await Notifications(s.ProjectId));
        await using (var db = fixture.CreateContext())
            Assert.False(await db.Notifications.AnyAsync(n => n.NotificationRecipients.Any(r => r.UserId == s.MemberId)));
        await Run(s.ProjectId);
        Assert.Contains(await Notifications(s.ProjectId), n => n.NotificationType == "TASK_DEADLINE_REMINDER");
    }

    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Injected failure after SQL save");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Only_current_primary_supervisor_receives_warnings(bool ended)
    {
        var s = await fixture.Seed();
        await TaskRow(s, Now.AddHours(-1), "BLOCKED");
        await using (var db = fixture.CreateContext())
        {
            var request = new M.SupervisorRequest { ProjectId = s.ProjectId, SupervisorProfileId = s.Users.ProfileId,
                RequestedBy = s.Users.Student, Status = "ACCEPTED", RequestedAt = Now.AddDays(-1), RespondedAt = Now.AddHours(-1) };
            db.SupervisorAssignments.Add(new() { ProjectId = s.ProjectId, SupervisorRequest = request,
                SupervisorProfileId = s.Users.ProfileId, IsPrimary = true, AssignedAt = Now.AddHours(-1), EndedAt = ended ? Now : null });
            await db.SaveChangesAsync();
        }
        await Run(s.ProjectId);
        var rows = await Notifications(s.ProjectId);
        Assert.Equal(!ended, rows.Where(n => n.NotificationType == "TASK_OVERDUE")
            .SelectMany(n => n.NotificationRecipients).Any(r => r.UserId == s.Users.Lecturer));
        Assert.DoesNotContain(rows.Where(n => n.NotificationType.EndsWith("REMINDER"))
            .SelectMany(n => n.NotificationRecipients), r => r.UserId == s.Users.Lecturer);
        Assert.DoesNotContain(rows.SelectMany(n => n.NotificationRecipients), r => r.UserId == s.Users.OtherLecturer || r.UserId == s.Users.Staff);
    }

    [Fact]
    public async Task Enabled_hosted_worker_delivers_to_existing_inbox_API()
    {
        var s = await fixture.Seed();
        using var app = new ReminderFactory(fixture.ConnectionString);
        using var user = app.CreateAuthenticatedClient(s.Users.Student);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        PagedResult<NotificationDto>? inbox;
        do
        {
            inbox = await user.GetFromJsonAsync<PagedResult<NotificationDto>>(
                "/api/v1/notifications?notificationType=FINAL_SUBMISSION_DEADLINE_REMINDER", timeout.Token);
            if (inbox!.Items.Any(n => n.RelatedEntityId == s.ProjectId)) break;
            await Task.Delay(50, timeout.Token);
        } while (true);
        var item = Assert.Single(inbox.Items.Where(n => n.RelatedEntityId == s.ProjectId));
        Assert.False(item.IsRead);
        Assert.Equal("PROJECT", item.RelatedEntityType);
        using var outsider = app.CreateAuthenticatedClient(s.Users.OtherLecturer);
        var otherInbox = await outsider.GetFromJsonAsync<PagedResult<NotificationDto>>("/api/v1/notifications", timeout.Token);
        Assert.DoesNotContain(otherInbox!.Items, n => n.Id == item.Id);
    }

    private sealed class ReminderFactory(string connection) : AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connection,
                ["ScheduledNotifications:Enabled"] = "true",
                ["ScheduledNotifications:BatchSize"] = "2"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedClock());
            });
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
}
