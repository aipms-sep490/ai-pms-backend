using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Services.Projects;
using Microsoft.EntityFrameworkCore;
using Xunit;
using AcademicSemester = AIPMS.Infrastructure.Persistence.Generated.Models.AcademicSemester;
using ProjectPeriod = AIPMS.Infrastructure.Persistence.Generated.Models.ProjectPeriod;
using Team = AIPMS.Infrastructure.Persistence.Generated.Models.Team;
using Project = AIPMS.Infrastructure.Persistence.Generated.Models.Project;
using Milestone = AIPMS.Infrastructure.Persistence.Generated.Models.Milestone;
using MilestoneDependency = AIPMS.Infrastructure.Persistence.Generated.Models.MilestoneDependency;
using TaskEntity = AIPMS.Infrastructure.Persistence.Generated.Models.Task;

namespace AIPMS.UnitTests.Application;

public sealed class MilestoneAndTaskBusinessRulesTests
{
    private static AipmsDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AipmsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AipmsDbContext(options);
    }

    private static async Task<(long ProjectId, long SemesterId, long M1Id, long M2Id)> SeedProjectWithExecutionWindowAsync(AipmsDbContext db)
    {
        var semester = new AcademicSemester
        {
            Id = 10,
            Code = "FA26",
            Name = "Fall 2026",
            StartDate = new DateOnly(2026, 9, 1),
            EndDate = new DateOnly(2026, 12, 31),
            Status = "ACTIVE",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.AcademicSemesters.Add(semester);

        var executionPeriod = new ProjectPeriod
        {
            Id = 100,
            AcademicSemesterId = 10,
            Code = "EXEC-FA26",
            PeriodType = "EXECUTION",
            Name = "Execution Period Fall 2026",
            StartAt = new DateTime(2026, 10, 31, 17, 0, 0, DateTimeKind.Utc),
            EndAt = new DateTime(2026, 11, 30, 16, 59, 59, DateTimeKind.Utc),
            Status = "ACTIVE",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.ProjectPeriods.Add(executionPeriod);

        var team = new Team
        {
            Id = 200,
            AcademicSemesterId = 10,
            Code = "TEAM-200",
            Name = "Team Alpha",
            Status = "ACTIVE",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Teams.Add(team);

        var project = new Project
        {
            Id = 300,
            TeamId = 200,
            Code = "PRJ-300",
            Title = "AI-PMS Capstone",
            Status = "ACTIVE",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            RowVersion = new byte[] { 1 }
        };
        db.Projects.Add(project);

        // M1: 2026-11-10 to 2026-11-20
        var m1 = new Milestone
        {
            Id = 401,
            ProjectId = 300,
            Title = "Milestone 1",
            StartDate = new DateOnly(2026, 11, 10),
            DueDate = new DateOnly(2026, 11, 20),
            Status = "IN_PROGRESS",
            SortOrder = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Milestones.Add(m1);

        // M2: 2026-11-21 to 2026-11-30
        var m2 = new Milestone
        {
            Id = 402,
            ProjectId = 300,
            Title = "Milestone 2",
            StartDate = new DateOnly(2026, 11, 21),
            DueDate = new DateOnly(2026, 11, 30),
            Status = "PLANNED",
            SortOrder = 2,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Milestones.Add(m2);

        await db.SaveChangesAsync();
        return (project.Id, semester.Id, m1.Id, m2.Id);
    }

    [Fact]
    public async Task ValidateTaskDates_DeadlineBeforeMilestoneStart_ThrowsConflictException()
    {
        await using var db = CreateInMemoryDbContext();
        var (projectId, _, m1Id, _) = await SeedProjectWithExecutionWindowAsync(db);
        var service = new ExecutionWindowService(db);

        // Milestone 1 is 10/11 - 20/11. Task deadline is 05/11.
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.ValidateTaskDatesAsync(
                projectId,
                milestoneId: m1Id,
                startAt: null,
                dueAt: new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(WorkflowErrorCodes.TaskOutsideMilestone, ex.Code);
    }

    [Fact]
    public async Task ValidateTaskDates_DeadlineAfterMilestoneDue_ThrowsConflictException()
    {
        await using var db = CreateInMemoryDbContext();
        var (projectId, _, m1Id, _) = await SeedProjectWithExecutionWindowAsync(db);
        var service = new ExecutionWindowService(db);

        // Milestone 1 is 10/11 - 20/11. Task deadline is 25/11.
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.ValidateTaskDatesAsync(
                projectId,
                milestoneId: m1Id,
                startAt: null,
                dueAt: new DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(WorkflowErrorCodes.TaskOutsideMilestone, ex.Code);
    }

    [Fact]
    public async Task ValidateTaskDates_DeadlineInsideMilestoneAndExecutionWindow_Succeeds()
    {
        await using var db = CreateInMemoryDbContext();
        var (projectId, _, m1Id, _) = await SeedProjectWithExecutionWindowAsync(db);
        var service = new ExecutionWindowService(db);

        // Milestone 1 is 10/11 - 20/11. Task deadline is 15/11.
        await service.ValidateTaskDatesAsync(
            projectId,
            milestoneId: m1Id,
            startAt: new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc),
            dueAt: new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task ValidateTaskDates_DeadlineOutsideExecutionWindow_ThrowsConflictException()
    {
        await using var db = CreateInMemoryDbContext();
        var (projectId, _, _, _) = await SeedProjectWithExecutionWindowAsync(db);

        // Create a milestone that spans into next month
        var wideMilestone = new Milestone
        {
            Id = 499,
            ProjectId = projectId,
            Title = "Wide Milestone",
            StartDate = new DateOnly(2026, 11, 1),
            DueDate = new DateOnly(2026, 12, 31),
            Status = "PLANNED",
            SortOrder = 9,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Milestones.Add(wideMilestone);
        await db.SaveChangesAsync();

        var service = new ExecutionWindowService(db);

        // Execution Window is 01/11 - 30/11. Task deadline is 15/12.
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.ValidateTaskDatesAsync(
                projectId,
                milestoneId: wideMilestone.Id,
                startAt: null,
                dueAt: new DateTime(2026, 12, 15, 12, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(WorkflowErrorCodes.TaskOutsideExecutionWindow, ex.Code);
    }

    [Fact]
    public async Task ValidateTaskDates_DueBeforeStart_ThrowsConflictException()
    {
        await using var db = CreateInMemoryDbContext();
        var (projectId, _, m1Id, _) = await SeedProjectWithExecutionWindowAsync(db);
        var service = new ExecutionWindowService(db);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.ValidateTaskDatesAsync(
                projectId,
                milestoneId: m1Id,
                startAt: new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc),
                dueAt: new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc)));

        Assert.Contains("earlier", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateMilestoneDates_OutsideExecutionWindow_ThrowsConflictException()
    {
        await using var db = CreateInMemoryDbContext();
        var (projectId, _, _, _) = await SeedProjectWithExecutionWindowAsync(db);
        var service = new ExecutionWindowService(db);

        // Execution window is 01/11 - 30/11. Milestone dates: 15/11 to 05/12.
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.ValidateMilestoneDatesAsync(
                projectId,
                startDate: new DateOnly(2026, 11, 15),
                dueDate: new DateOnly(2026, 12, 5)));

        Assert.Equal(WorkflowErrorCodes.MilestoneOutsideExecutionWindow, ex.Code);
    }

    [Fact]
    public async Task ValidateMilestoneDates_IndependentMilestonesOverlapping_Succeeds()
    {
        await using var db = CreateInMemoryDbContext();
        var (projectId, _, _, _) = await SeedProjectWithExecutionWindowAsync(db);
        var service = new ExecutionWindowService(db);

        // M1: 10/11 - 20/11 (already in DB)
        // M3 (overlapping with M1): 15/11 - 25/11.
        // Both are inside Execution Window (01/11 - 30/11). Overlap is allowed!
        await service.ValidateMilestoneDatesAsync(
            projectId,
            startDate: new DateOnly(2026, 11, 15),
            dueDate: new DateOnly(2026, 11, 25));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Single_milestone_date_must_be_inside_both_execution_boundaries(bool startOnly)
    {
        await using var db = CreateInMemoryDbContext();
        var (projectId, _, _, _) = await SeedProjectWithExecutionWindowAsync(db);
        var service = new ExecutionWindowService(db);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => service.ValidateMilestoneDatesAsync(
            projectId, startOnly ? new DateOnly(2026, 12, 1) : null,
            startOnly ? null : new DateOnly(2026, 10, 31)));
        Assert.Equal(WorkflowErrorCodes.MilestoneOutsideExecutionWindow, ex.Code);
    }

    [Fact]
    public async Task Changing_unrelated_execution_period_does_not_validate_other_periods_projects()
    {
        await using var db = CreateInMemoryDbContext();
        await SeedProjectWithExecutionWindowAsync(db);
        db.ProjectPeriods.Add(new ProjectPeriod
        {
            Id = 101, AcademicSemesterId = 10, Code = "OLD-EXEC", Name = "Other execution period",
            PeriodType = "EXECUTION", Status = "CLOSED", StartAt = new DateTime(2026, 9, 1),
            EndAt = new DateTime(2026, 9, 30)
        });
        await db.SaveChangesAsync();
        await new ExecutionWindowService(db).ValidateExecutionWindowUpdateImpactAsync(
            101, new DateTime(2026, 9, 1), new DateTime(2026, 9, 15));
    }

    [Fact]
    public async Task Task_and_milestone_boundaries_use_Vietnam_calendar_date()
    {
        await using var db = CreateInMemoryDbContext();
        var (projectId, _, m1Id, _) = await SeedProjectWithExecutionWindowAsync(db);
        var service = new ExecutionWindowService(db);
        // 10 November 00:30 Vietnam is still 9 November in UTC.
        await service.ValidateTaskDatesAsync(projectId, m1Id, new DateTime(2026, 11, 9, 17, 30, 0), null);
        // 21 November 00:30 Vietnam is beyond the milestone's final date (20th).
        var error = await Assert.ThrowsAsync<ConflictException>(() => service.ValidateTaskDatesAsync(
            projectId, m1Id, null, new DateTime(2026, 11, 20, 17, 30, 0)));
        Assert.Equal(WorkflowErrorCodes.TaskOutsideMilestone, error.Code);
        await service.ValidateMilestoneDateUpdateImpactAsync(m1Id, new(2026, 11, 10), new(2026, 11, 20));
    }

    [Fact]
    public async Task ValidateMilestoneDateUpdateImpact_ChildTaskFallsOutside_ThrowsConflictException()
    {
        await using var db = CreateInMemoryDbContext();
        var (projectId, _, m1Id, _) = await SeedProjectWithExecutionWindowAsync(db);

        // Add a child task to M1 with deadline 18/11
        var task = new TaskEntity
        {
            Id = 501,
            MilestoneId = m1Id,
            Title = "Task in M1",
            Status = "IN_PROGRESS",
            Priority = "MEDIUM",
            StartAt = new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc),
            DueAt = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var service = new ExecutionWindowService(db);

        // User tries to shorten M1 due date to 15/11. Task deadline 18/11 is now outside!
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.ValidateMilestoneDateUpdateImpactAsync(
                m1Id,
                newStartDate: new DateOnly(2026, 11, 10),
                newDueDate: new DateOnly(2026, 11, 15)));

        Assert.Equal(WorkflowErrorCodes.ParentDateConflict, ex.Code);
        Assert.Contains("501", ex.Message);
    }

    [Fact]
    public async Task ValidateMilestoneDateUpdateImpact_DependentMilestoneOrderingViolation_ThrowsConflictException()
    {
        await using var db = CreateInMemoryDbContext();
        var (projectId, _, m1Id, m2Id) = await SeedProjectWithExecutionWindowAsync(db);

        // Add Finish-to-Start dependency: M2 depends on M1.
        // M1 DueDate is 20/11, M2 StartDate is 21/11.
        var dep = new MilestoneDependency
        {
            MilestoneId = m2Id,
            DependsOnMilestoneId = m1Id,
            DependencyType = "FINISH_TO_START",
            CreatedAt = DateTime.UtcNow
        };
        db.MilestoneDependencies.Add(dep);
        await db.SaveChangesAsync();

        var service = new ExecutionWindowService(db);

        // User tries to extend M1 due date to 25/11. M2 starts on 21/11 -> ordering violation!
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.ValidateMilestoneDateUpdateImpactAsync(
                m1Id,
                newStartDate: new DateOnly(2026, 11, 10),
                newDueDate: new DateOnly(2026, 11, 25)));

        Assert.Equal(WorkflowErrorCodes.MilestoneDependencyOrderViolation, ex.Code);
    }

    [Fact]
    public async Task ValidateExecutionWindowUpdateImpact_ExistingMilestoneFallsOutside_ThrowsConflictException()
    {
        await using var db = CreateInMemoryDbContext();
        var (projectId, semesterId, _, m2Id) = await SeedProjectWithExecutionWindowAsync(db);
        var service = new ExecutionWindowService(db);

        // Execution period has Id = 100. M2 ends on 2026-11-30. User tries to shorten execution period to 2026-11-25.
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.ValidateExecutionWindowUpdateImpactAsync(
                projectPeriodId: 100,
                newStartAt: new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                newEndAt: new DateTime(2026, 11, 25, 23, 59, 59, DateTimeKind.Utc)));

        Assert.Equal(WorkflowErrorCodes.ParentDateConflict, ex.Code);
        Assert.Contains(m2Id.ToString(), ex.Message);
    }
}
