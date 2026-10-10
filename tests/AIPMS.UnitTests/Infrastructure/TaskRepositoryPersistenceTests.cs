using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Infrastructure.Identity;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.Infrastructure.Persistence.Repositories;
using AIPMS.Infrastructure.Services.Projects;
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

    [Fact]
    public async System.Threading.Tasks.Task GetTasksAsync_SingleMajor_ReturnsOnlyMappedTasks()
    {
        using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        await SeedBaseProjectAndMilestoneAsync(db, projectId: 1, milestoneId: 1);

        var creator = CreateUser(10, "creator@test.com");
        db.Users.Add(creator);

        db.Tasks.AddRange(
            new TaskEntity { Id = 1, MilestoneId = 1, Title = "Task A", Status = "TODO", Priority = "NORMAL", CreatedBy = 10 },
            new TaskEntity { Id = 2, MilestoneId = 1, Title = "Task B", Status = "TODO", Priority = "NORMAL", CreatedBy = 10 }
        );
        db.TaskDisciplines.Add(new TaskDiscipline { TaskId = 1, MajorId = 100, Role = "PRIMARY", CreatedBy = 10, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var repo = new TaskRepository(db);
        var result = await repo.GetTasksAsync(
            projectId: 1, milestoneId: null, status: null, priority: null,
            assigneeUserId: null, search: null, dueFrom: null, dueTo: null,
            isOverdue: null, isBlocked: null, page: 1, pageSize: 10, majorId: 100,
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal(1, result.Items[0].Id);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetTasksAsync_Interdisciplinary_SeparatesMajors()
    {
        using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        await SeedBaseProjectAndMilestoneAsync(db, projectId: 1, milestoneId: 1);

        var creator = CreateUser(10, "creator@test.com");
        db.Users.Add(creator);

        db.Tasks.AddRange(
            new TaskEntity { Id = 1, MilestoneId = 1, Title = "Task A", Status = "TODO", Priority = "NORMAL", CreatedBy = 10 },
            new TaskEntity { Id = 2, MilestoneId = 1, Title = "Task B", Status = "TODO", Priority = "NORMAL", CreatedBy = 10 }
        );
        db.TaskDisciplines.AddRange(
            new TaskDiscipline { TaskId = 1, MajorId = 100, Role = "PRIMARY", CreatedBy = 10, CreatedAt = DateTime.UtcNow },
            new TaskDiscipline { TaskId = 2, MajorId = 200, Role = "PRIMARY", CreatedBy = 10, CreatedAt = DateTime.UtcNow }
        );
        await db.SaveChangesAsync();

        var repo = new TaskRepository(db);

        var resA = await repo.GetTasksAsync(1, null, null, null, null, null, null, null, null, null, 1, 10, 100, CancellationToken.None);
        Assert.Equal(1, resA.TotalCount);
        Assert.Single(resA.Items);
        Assert.Equal(1, resA.Items[0].Id);

        var resB = await repo.GetTasksAsync(1, null, null, null, null, null, null, null, null, null, 1, 10, 200, CancellationToken.None);
        Assert.Equal(1, resB.TotalCount);
        Assert.Single(resB.Items);
        Assert.Equal(2, resB.Items[0].Id);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetTasksAsync_MultiDiscipline_DoesNotDuplicateTask()
    {
        using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        await SeedBaseProjectAndMilestoneAsync(db, projectId: 1, milestoneId: 1);

        var creator = CreateUser(10, "creator@test.com");
        db.Users.Add(creator);

        db.Tasks.Add(new TaskEntity { Id = 1, MilestoneId = 1, Title = "Task Shared", Status = "TODO", Priority = "NORMAL", CreatedBy = 10 });
        db.TaskDisciplines.AddRange(
            new TaskDiscipline { TaskId = 1, MajorId = 100, Role = "PRIMARY", CreatedBy = 10, CreatedAt = DateTime.UtcNow },
            new TaskDiscipline { TaskId = 1, MajorId = 200, Role = "SECONDARY", CreatedBy = 10, CreatedAt = DateTime.UtcNow }
        );
        await db.SaveChangesAsync();

        var repo = new TaskRepository(db);

        var resA = await repo.GetTasksAsync(1, null, null, null, null, null, null, null, null, null, 1, 10, 100, CancellationToken.None);
        Assert.Equal(1, resA.TotalCount);
        Assert.Single(resA.Items);
        Assert.Equal(1, resA.Items[0].Id);

        var resB = await repo.GetTasksAsync(1, null, null, null, null, null, null, null, null, null, 1, 10, 200, CancellationToken.None);
        Assert.Equal(1, resB.TotalCount);
        Assert.Single(resB.Items);
        Assert.Equal(1, resB.Items[0].Id);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetTasksAsync_PaginationAndTotalCount_ReflectsMajorFilter()
    {
        using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        await SeedBaseProjectAndMilestoneAsync(db, projectId: 1, milestoneId: 1);

        var creator = CreateUser(10, "creator@test.com");
        db.Users.Add(creator);

        db.Tasks.AddRange(
            new TaskEntity { Id = 1, MilestoneId = 1, Title = "Task 1", Status = "TODO", Priority = "NORMAL", CreatedBy = 10 },
            new TaskEntity { Id = 2, MilestoneId = 1, Title = "Task 2", Status = "TODO", Priority = "NORMAL", CreatedBy = 10 },
            new TaskEntity { Id = 3, MilestoneId = 1, Title = "Task 3", Status = "TODO", Priority = "NORMAL", CreatedBy = 10 },
            new TaskEntity { Id = 4, MilestoneId = 1, Title = "Task 4", Status = "TODO", Priority = "NORMAL", CreatedBy = 10 },
            new TaskEntity { Id = 5, MilestoneId = 1, Title = "Task 5", Status = "TODO", Priority = "NORMAL", CreatedBy = 10 }
        );
        db.TaskDisciplines.AddRange(
            new TaskDiscipline { TaskId = 1, MajorId = 100, Role = "PRIMARY", CreatedBy = 10, CreatedAt = DateTime.UtcNow },
            new TaskDiscipline { TaskId = 2, MajorId = 100, Role = "PRIMARY", CreatedBy = 10, CreatedAt = DateTime.UtcNow },
            new TaskDiscipline { TaskId = 3, MajorId = 100, Role = "PRIMARY", CreatedBy = 10, CreatedAt = DateTime.UtcNow },
            new TaskDiscipline { TaskId = 4, MajorId = 200, Role = "PRIMARY", CreatedBy = 10, CreatedAt = DateTime.UtcNow },
            new TaskDiscipline { TaskId = 5, MajorId = 200, Role = "PRIMARY", CreatedBy = 10, CreatedAt = DateTime.UtcNow }
        );
        await db.SaveChangesAsync();

        var repo = new TaskRepository(db);

        var p1 = await repo.GetTasksAsync(1, null, null, null, null, null, null, null, null, null, 1, 2, 100, CancellationToken.None);
        Assert.Equal(3, p1.TotalCount);
        Assert.Equal(2, p1.TotalPages);
        Assert.Equal(2, p1.Items.Count);
        Assert.Equal(1, p1.Items[0].Id);
        Assert.Equal(2, p1.Items[1].Id);

        var p2 = await repo.GetTasksAsync(1, null, null, null, null, null, null, null, null, null, 2, 2, 100, CancellationToken.None);
        Assert.Equal(3, p2.TotalCount);
        Assert.Equal(2, p2.TotalPages);
        Assert.Single(p2.Items);
        Assert.Equal(3, p2.Items[0].Id);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetTasksAsync_CombinedFilter_AppliesMajorAndStatusPredicates()
    {
        using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        await SeedBaseProjectAndMilestoneAsync(db, projectId: 1, milestoneId: 1);

        var creator = CreateUser(10, "creator@test.com");
        db.Users.Add(creator);

        db.Tasks.AddRange(
            new TaskEntity { Id = 1, MilestoneId = 1, Title = "Backend Auth", Status = "TODO", Priority = "NORMAL", CreatedBy = 10 },
            new TaskEntity { Id = 2, MilestoneId = 1, Title = "Database Indexes", Status = "DONE", Priority = "NORMAL", CreatedBy = 10 },
            new TaskEntity { Id = 3, MilestoneId = 1, Title = "Frontend App", Status = "TODO", Priority = "NORMAL", CreatedBy = 10 }
        );
        db.TaskDisciplines.AddRange(
            new TaskDiscipline { TaskId = 1, MajorId = 100, Role = "PRIMARY", CreatedBy = 10, CreatedAt = DateTime.UtcNow },
            new TaskDiscipline { TaskId = 2, MajorId = 100, Role = "PRIMARY", CreatedBy = 10, CreatedAt = DateTime.UtcNow },
            new TaskDiscipline { TaskId = 3, MajorId = 200, Role = "PRIMARY", CreatedBy = 10, CreatedAt = DateTime.UtcNow }
        );
        await db.SaveChangesAsync();

        var repo = new TaskRepository(db);

        // Major + status
        var statusRes = await repo.GetTasksAsync(1, null, "TODO", null, null, null, null, null, null, null, 1, 10, 100, CancellationToken.None);
        Assert.Equal(1, statusRes.TotalCount);
        Assert.Single(statusRes.Items);
        Assert.Equal(1, statusRes.Items[0].Id);

        // Major + search
        var searchRes = await repo.GetTasksAsync(1, null, null, null, null, "Database", null, null, null, null, 1, 10, 100, CancellationToken.None);
        Assert.Equal(1, searchRes.TotalCount);
        Assert.Single(searchRes.Items);
        Assert.Equal(2, searchRes.Items[0].Id);
    }

    [Fact]
    public async System.Threading.Tasks.Task MajorBelongsToProjectAsync_DraftProject_UsesCurrentProjectMajors()
    {
        using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        await SeedBaseProjectAndMilestoneAsync(db, projectId: 1, milestoneId: 1, projectStatus: "DRAFT");
        await SeedBaseProjectAndMilestoneAsync(db, projectId: 2, milestoneId: 2, projectStatus: "DRAFT");

        db.Majors.AddRange(
            new Major { Id = 10, DepartmentId = 1, Code = "M10", Name = "Major 10" },
            new Major { Id = 20, DepartmentId = 1, Code = "M20", Name = "Major 20" }
        );
        db.ProjectMajors.AddRange(
            new ProjectMajor { Id = 1, ProjectId = 1, MajorId = 10, CreatedAt = DateTime.UtcNow },
            new ProjectMajor { Id = 2, ProjectId = 2, MajorId = 20, CreatedAt = DateTime.UtcNow }
        );
        await db.SaveChangesAsync();

        var repo = new TaskRepository(db);

        Assert.True(await repo.MajorBelongsToProjectAsync(10, 1, CancellationToken.None));
        Assert.False(await repo.MajorBelongsToProjectAsync(20, 1, CancellationToken.None));
        Assert.True(await repo.MajorBelongsToProjectAsync(20, 2, CancellationToken.None));
        Assert.False(await repo.MajorBelongsToProjectAsync(10, 2, CancellationToken.None));
    }

    [Fact]
    public async System.Threading.Tasks.Task MajorBelongsToProjectAsync_ActiveProject_UsesFrozenScope_IgnoresProjectMajors()
    {
        using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        await SeedBaseProjectAndMilestoneAsync(db, projectId: 1, milestoneId: 1, projectStatus: "ACTIVE");

        // ProjectMajors has both Major 10 and Major 20
        db.ProjectMajors.AddRange(
            new ProjectMajor { Id = 1, ProjectId = 1, MajorId = 10, CreatedAt = DateTime.UtcNow },
            new ProjectMajor { Id = 2, ProjectId = 1, MajorId = 20, CreatedAt = DateTime.UtcNow }
        );

        // But frozen registration snapshot strictly contains ONLY Major 10
        var evidence = new RegistrationEvidence(
            new TeamAcademicScopeDto("SINGLE_MAJOR", 10, 100, [new MajorRequirementDto(10, 1, 5, "Engineering")], Guid.NewGuid()),
            new(1, 5, 1, "test"), 1, DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(2),
            [new(1, "Student", 10, true)], [100],
            MajorDepartmentIds: new Dictionary<long, long> { [10] = 100 });

        db.Set<ProjectRegistrationSnapshot>().Add(new ProjectRegistrationSnapshot
        {
            Id = 1,
            ProjectId = 1,
            ProjectPeriodId = 1,
            LeadDepartmentId = 100,
            SubmittedBy = 1,
            SubmittedAt = DateTime.UtcNow.AddDays(-1),
            SnapshotJson = JsonSerializer.Serialize(evidence)
        });
        await db.SaveChangesAsync();

        var repo = new TaskRepository(db);

        // Major 10 is in frozen snapshot -> True
        Assert.True(await repo.MajorBelongsToProjectAsync(10, 1, CancellationToken.None));

        // Major 20 is only in live ProjectMajors, not in snapshot -> False (no fallback to ProjectMajors)
        Assert.False(await repo.MajorBelongsToProjectAsync(20, 1, CancellationToken.None));
    }

    [Fact]
    public async System.Threading.Tasks.Task MajorBelongsToProjectAsync_ActiveProject_MissingOrInvalidSnapshot_FailsClosed()
    {
        using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        await SeedBaseProjectAndMilestoneAsync(db, projectId: 1, milestoneId: 1, projectStatus: "ACTIVE");

        // ProjectMajors has Major 10, but NO frozen snapshot exists for non-draft project
        db.ProjectMajors.Add(new ProjectMajor { Id = 1, ProjectId = 1, MajorId = 10, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var repo = new TaskRepository(db);

        // Missing snapshot for non-draft project fails closed
        Assert.False(await repo.MajorBelongsToProjectAsync(10, 1, CancellationToken.None));
    }
}
