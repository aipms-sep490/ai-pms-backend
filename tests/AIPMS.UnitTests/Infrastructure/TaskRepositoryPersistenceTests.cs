using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Infrastructure.Identity;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;
using TaskEntity = AIPMS.Infrastructure.Persistence.Generated.Models.Task;

namespace AIPMS.UnitTests.Infrastructure;

public sealed class TaskRepositoryPersistenceTests
{
    private static AipmsDbContext CreateInMemoryDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AipmsDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new AipmsDbContext(options);
    }

    private static User CreateUser(long id, string email) => new User
    {
        Id = id,
        Email = email,
        FullName = $"User {id}",
        PasswordHash = "hashedpassword",
        Status = "ACTIVE"
    };

    private static async System.Threading.Tasks.Task SeedBaseProjectAndMilestoneAsync(AipmsDbContext db, long projectId = 1, long milestoneId = 1, string projectStatus = "ACTIVE")
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId))
        {
            var project = new Project
            {
                Id = projectId,
                Code = $"PRJ-{projectId}",
                Title = $"Project {projectId}",
                Status = projectStatus,
                RowVersion = new byte[] { 1 }
            };
            db.Projects.Add(project);
        }

        if (!await db.Milestones.AnyAsync(m => m.Id == milestoneId))
        {
            var milestone = new Milestone { Id = milestoneId, ProjectId = projectId, Title = $"Milestone {milestoneId}", Status = "IN_PROGRESS" };
            db.Milestones.Add(milestone);
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async System.Threading.Tasks.Task GetTasksAsync_PaginationMetadata_ReturnsCorrectPageAndTotalPages()
    {
        using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        await SeedBaseProjectAndMilestoneAsync(db, projectId: 1, milestoneId: 1);

        var creator = CreateUser(10, "creator@test.com");
        db.Users.Add(creator);

        // Seed 3 tasks
        db.Tasks.AddRange(
            new TaskEntity { Id = 1, MilestoneId = 1, Title = "Task 1", Status = "TODO", Priority = "NORMAL", CreatedBy = 10 },
            new TaskEntity { Id = 2, MilestoneId = 1, Title = "Task 2", Status = "TODO", Priority = "NORMAL", CreatedBy = 10 },
            new TaskEntity { Id = 3, MilestoneId = 1, Title = "Task 3", Status = "TODO", Priority = "NORMAL", CreatedBy = 10 }
        );
        await db.SaveChangesAsync();

        var repository = new TaskRepository(db);

        // Request Page = 2, PageSize = 1
        var result = await repository.GetTasksAsync(
            projectId: 1, milestoneId: null, status: null, priority: null,
            assigneeUserId: null, search: null, dueFrom: null, dueTo: null,
            isOverdue: null, isBlocked: null, page: 2, pageSize: 1, cancellationToken: CancellationToken.None);

        Assert.Equal(2, result.Page);
        Assert.Equal(1, result.PageSize);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.TotalPages); // ceil(3 / 1) = 3
        Assert.Single(result.Items);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetTasksAsync_EmptyResult_ReturnsZeroTotalCountAndTotalPages()
    {
        using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        await SeedBaseProjectAndMilestoneAsync(db, projectId: 1, milestoneId: 1);

        var repository = new TaskRepository(db);

        // Query for a non-existent project (999)
        var result = await repository.GetTasksAsync(
            projectId: 999, milestoneId: null, status: null, priority: null,
            assigneeUserId: null, search: null, dueFrom: null, dueTo: null,
            isOverdue: null, isBlocked: null, page: 1, pageSize: 10, cancellationToken: CancellationToken.None);

        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async System.Threading.Tasks.Task IsUserActiveTeamMemberAsync_ChecksLeftAt()
    {
        using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());

        var team = new Team { Id = 1, AcademicSemesterId = 1, Code = "TM-1", Name = "Team A", Status = "ACTIVE" };
        var activeMember = new TeamMember { Id = 1, TeamId = 1, AcademicSemesterId = 1, UserId = 10, LeftAt = null };
        var formerMember = new TeamMember { Id = 2, TeamId = 1, AcademicSemesterId = 1, UserId = 20, LeftAt = DateTime.UtcNow.AddDays(-1) };
        var project = new Project { Id = 1, TeamId = 1, Code = "PRJ-1", Title = "Project 1", Status = "ACTIVE", RowVersion = new byte[] { 1 } };

        db.Teams.Add(team);
        db.TeamMembers.AddRange(activeMember, formerMember);
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var repository = new TaskRepository(db);

        var isActive10 = await repository.IsUserActiveTeamMemberAsync(1, 10, CancellationToken.None);
        var isActive20 = await repository.IsUserActiveTeamMemberAsync(1, 20, CancellationToken.None);

        Assert.True(isActive10);
        Assert.False(isActive20);
    }

    [Theory]
    [InlineData("COMPLETED")]
    [InlineData("ARCHIVED")]
    public async System.Threading.Tasks.Task ProjectExecutionGuard_RestrictsMutationsForNonActiveProject(string nonActiveStatus)
    {
        using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        await SeedBaseProjectAndMilestoneAsync(db, projectId: 1, milestoneId: 1, projectStatus: nonActiveStatus);

        db.Tasks.Add(new TaskEntity { Id = 10, MilestoneId = 1, Title = "Task on Non-Active Project", Status = "TODO", CreatedBy = 1 });
        await db.SaveChangesAsync();

        var guard = new ProjectExecutionGuard(db);

        await Assert.ThrowsAsync<ConflictException>(() => guard.MustBeActiveAsync(1, CancellationToken.None));
        await Assert.ThrowsAsync<ConflictException>(() => guard.MustBeActiveForMilestoneAsync(1, CancellationToken.None));
        await Assert.ThrowsAsync<ConflictException>(() => guard.MustBeActiveForTaskAsync(10, CancellationToken.None));
    }

    [Fact]
    public async System.Threading.Tasks.Task CreateAsync_SavesTaskAndAssigneesInSingleTransaction()
    {
        using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        await SeedBaseProjectAndMilestoneAsync(db, projectId: 1, milestoneId: 1);

        var user1 = CreateUser(10, "u10@test.com");
        var user2 = CreateUser(20, "u20@test.com");
        db.Users.AddRange(user1, user2);
        await db.SaveChangesAsync();

        var repository = new TaskRepository(db);

        var dto = await repository.CreateAsync(
            milestoneId: 1,
            parentTaskId: null,
            title: "Task with Assignees",
            description: "Desc",
            priority: "HIGH",
            startAt: null,
            dueAt: null,
            assigneeUserIds: new[] { 10L, 20L },
            createdByUserId: 10,
            cancellationToken: CancellationToken.None);

        Assert.NotNull(dto);

        // Verify direct EF persistence: task created & 2 assignees added
        var savedTask = await db.Tasks.Include(t => t.TaskAssignees).FirstOrDefaultAsync(t => t.Id == dto.Id);
        Assert.NotNull(savedTask);
        Assert.Equal("Task with Assignees", savedTask.Title);
        Assert.Equal(2, savedTask.TaskAssignees.Count);
        Assert.Contains(savedTask.TaskAssignees, ta => ta.UserId == 10);
        Assert.Contains(savedTask.TaskAssignees, ta => ta.UserId == 20);
    }

    [Fact]
    public async System.Threading.Tasks.Task UpdateStatusAsync_UpdatesTaskAndRecordsStatusHistory()
    {
        using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        await SeedBaseProjectAndMilestoneAsync(db, projectId: 1, milestoneId: 1);

        var task = new TaskEntity { Id = 5, MilestoneId = 1, Title = "Status Task", Status = "TODO", CreatedBy = 10 };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var repository = new TaskRepository(db);

        await repository.UpdateStatusAsync(
            taskId: 5,
            newStatus: "IN_PROGRESS",
            reason: "Starting task execution",
            actorUserId: 10,
            cancellationToken: CancellationToken.None);

        // Verify task status updated in DB
        var updatedTask = await db.Tasks.FirstAsync(t => t.Id == 5);
        Assert.Equal("IN_PROGRESS", updatedTask.Status);

        // Verify TaskStatusHistory record created
        var history = await db.TaskStatusHistories.Where(h => h.TaskId == 5).ToListAsync();
        Assert.Single(history);
        Assert.Equal("TODO", history[0].OldStatus);
        Assert.Equal("IN_PROGRESS", history[0].NewStatus);
        Assert.Equal("Starting task execution", history[0].Reason);
        Assert.Equal(10, history[0].ChangedBy);
    }
}
