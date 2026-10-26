using System;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Task = System.Threading.Tasks.Task;
using TaskEntity = AIPMS.Infrastructure.Persistence.Generated.Models.Task;

namespace AIPMS.UnitTests.Repositories;

public sealed class ProgressReportPrefillRepositoryTests
{
    private static AipmsDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<AipmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AipmsDbContext(options);
    }

    [Fact]
    public async Task GetPrefillDataAsync_Includes_DoneTasks_InPeriod_And_Excludes_OutsidePeriod()
    {
        await using var db = CreateInMemoryContext();

        var periodStart = new DateOnly(2026, 10, 1);
        var periodEnd = new DateOnly(2026, 10, 15);
        long projectId = 100;

        var milestone = new Milestone
        {
            Id = 1,
            ProjectId = projectId,
            Title = "Sprint 1 Milestone",
            Status = "ACTIVE",
            SortOrder = 1,
            ConcurrencyToken = Guid.NewGuid()
        };
        db.Milestones.Add(milestone);

        // 1. Task DONE inside period
        db.Tasks.Add(new TaskEntity
        {
            Id = 101,
            MilestoneId = 1,
            Title = "Task Done Inside Period",
            Status = "DONE",
            CompletedAt = new DateTime(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc),
            DueAt = new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc),
            ConcurrencyToken = Guid.NewGuid()
        });

        // 2. Task DONE outside period (too early)
        db.Tasks.Add(new TaskEntity
        {
            Id = 102,
            MilestoneId = 1,
            Title = "Task Done Prior To Period",
            Status = "DONE",
            CompletedAt = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc),
            DueAt = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc),
            ConcurrencyToken = Guid.NewGuid()
        });

        // 3. Task IN_PROGRESS
        db.Tasks.Add(new TaskEntity
        {
            Id = 103,
            MilestoneId = 1,
            Title = "Ongoing Task",
            Status = "IN_PROGRESS",
            DueAt = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc),
            ConcurrencyToken = Guid.NewGuid()
        });

        // 4. Meeting inside period
        db.Meetings.Add(new Meeting
        {
            Id = 201,
            ProjectId = projectId,
            Title = "Weekly Sync With Supervisor",
            StartAt = new DateTime(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc),
            Status = "COMPLETED",
            ConcurrencyToken = Guid.NewGuid()
        });

        // 5. Risk history in period
        db.Set<RiskHistory>().Add(new RiskHistory
        {
            Id = 301,
            ProjectId = projectId,
            RiskLevel = "MEDIUM",
            RiskScore = 4.5m,
            OverdueTasks = 1,
            BlockedTasks = 0,
            UnassignedTasks = 0,
            ProgressPercentage = 45m,
            RecordedAt = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc)
        });

        await db.SaveChangesAsync();

        var repo = new ProgressReportRepository(db);
        var result = await repo.GetPrefillDataAsync(projectId, 1, periodStart, periodEnd, "PROGRESS_REPORT", CancellationToken.None);

        Assert.False(result.InsufficientData);

        // Completed work should only contain Task 101, NOT Task 102
        Assert.NotNull(result.Suggested.CompletedWork);
        Assert.Contains("Task Done Inside Period", result.Suggested.CompletedWork);
        Assert.DoesNotContain("Task Done Prior To Period", result.Suggested.CompletedWork);

        // In progress
        Assert.NotNull(result.Suggested.InProgressWork);
        Assert.Contains("Ongoing Task", result.Suggested.InProgressWork);

        // Meeting in summary & evidence
        Assert.NotNull(result.Suggested.Summary);
        Assert.Contains("1 cuộc họp", result.Suggested.Summary);
        Assert.Contains("1 công việc", result.Suggested.Summary);
        Assert.Contains(result.Evidence, e => e.Type == "MEETING" && e.Id == 201);

        // Risk in suggested & evidence
        Assert.NotNull(result.Suggested.Risks);
        Assert.Contains("MEDIUM", result.Suggested.Risks);
        Assert.Contains(result.Evidence, e => e.Type == "RISK" && e.Id == 301);
    }
}
