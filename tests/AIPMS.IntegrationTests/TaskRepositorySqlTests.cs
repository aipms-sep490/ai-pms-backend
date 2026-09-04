using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Infrastructure.Identity;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Task = System.Threading.Tasks.Task;
using TaskEntity = AIPMS.Infrastructure.Persistence.Generated.Models.Task;

namespace AIPMS.IntegrationTests;

[Collection("ProjectDbTests")]
public class TaskRepositorySqlTests
{
    private readonly DbFixture _fixture;

    public TaskRepositorySqlTests(DbFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<(long projectId, long milestoneId, long userId)> SeedProjectAndMilestoneAsync(AipmsDbContext context)
    {
        var semId = await context.AcademicSemesters.Select(s => s.Id).FirstAsync();
        var userId = await context.Users.Where(u => u.Email == "student1@aipms.test").Select(u => u.Id).FirstAsync();

        var team = new Team
        {
            AcademicSemesterId = semId,
            Code = "TM_" + Guid.NewGuid().ToString("N")[..8],
            Name = "Test Team " + Guid.NewGuid().ToString("N")[..6],
            Status = "ELIGIBLE",
            CreatedBy = userId
        };
        context.Teams.Add(team);
        await context.SaveChangesAsync();

        var project = new Project
        {
            TeamId = team.Id,
            Code = "PRJ_SQL_" + Guid.NewGuid().ToString("N")[..8],
            Title = "SQL Task Test Project " + Guid.NewGuid().ToString("N")[..6],
            Status = "ACTIVE",
            CreatedBy = userId,
            RowVersion = new byte[] { 1 }
        };
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        var milestone = new Milestone
        {
            ProjectId = project.Id,
            Title = "Milestone 1",
            Status = "IN_PROGRESS",
            CreatedBy = userId,
            SortOrder = 0
        };
        context.Milestones.Add(milestone);
        await context.SaveChangesAsync();

        return (project.Id, milestone.Id, userId);
    }

    [Fact]
    public async Task CreateTask_WhenAssigneePersistenceFails_RollsBackTaskAndAssignee()
    {
        using var context = _fixture.CreateContext();
        var (projectId, milestoneId, userId) = await SeedProjectAndMilestoneAsync(context);

        var repo = new TaskRepository(context);
        var taskTitle = "Rollback Task Test " + Guid.NewGuid().ToString("N")[..8];
        const long invalidAssigneeUserId = 9999999L; // Does not exist in users table -> FK violation

        // Assert that the operation throws DbUpdateException due to fk_task_assignees_user
        await Assert.ThrowsAnyAsync<DbUpdateException>(async () =>
        {
            await repo.CreateAsync(
                milestoneId: milestoneId,
                parentTaskId: null,
                title: taskTitle,
                description: "Should be rolled back",
                priority: "HIGH",
                startAt: null,
                dueAt: null,
                assigneeUserIds: new[] { invalidAssigneeUserId },
                createdByUserId: userId,
                cancellationToken: CancellationToken.None);
        });

        // Use a brand new DbContext to verify database state after rollback
        using var verifyContext = _fixture.CreateContext();
        var taskExists = await verifyContext.Tasks.AnyAsync(t => t.Title == taskTitle);
        var assigneeExists = await verifyContext.TaskAssignees.AnyAsync(ta => ta.UserId == invalidAssigneeUserId);

        Assert.False(taskExists, "Task must not exist in SQL Server after rollback.");
        Assert.False(assigneeExists, "TaskAssignee must not exist in SQL Server after rollback.");
    }

    [Fact]
    public async Task UpdateTaskStatus_WhenHistoryPersistenceFails_RollsBackStatusAndHistory()
    {
        using var context = _fixture.CreateContext();
        var (projectId, milestoneId, userId) = await SeedProjectAndMilestoneAsync(context);

        // Seed a valid task with TODO status
        var task = new TaskEntity
        {
            MilestoneId = milestoneId,
            Title = "Status Rollback Task " + Guid.NewGuid().ToString("N")[..8],
            Status = "TODO",
            Priority = "MEDIUM",
            CreatedBy = userId,
            CompletedAt = null
        };
        context.Tasks.Add(task);
        await context.SaveChangesAsync();

        var repo = new TaskRepository(context);
        const long invalidActorUserId = 9999999L; // Does not exist in users table -> FK violation on changed_by

        // Assert that update throws DbUpdateException due to fk_task_status_histories_user
        await Assert.ThrowsAnyAsync<DbUpdateException>(async () =>
        {
            await repo.UpdateStatusAsync(
                taskId: task.Id,
                newStatus: "IN_PROGRESS",
                reason: "Should be rolled back",
                actorUserId: invalidActorUserId,
                cancellationToken: CancellationToken.None);
        });

        // Use a fresh DbContext to verify task state remains unchanged in SQL Server
        using var verifyContext = _fixture.CreateContext();
        var reloadedTask = await verifyContext.Tasks.FirstAsync(t => t.Id == task.Id);

        Assert.Equal("TODO", reloadedTask.Status);
        Assert.Null(reloadedTask.CompletedAt);

        var historyExists = await verifyContext.TaskStatusHistories
            .AnyAsync(h => h.TaskId == task.Id && h.NewStatus == "IN_PROGRESS");
        Assert.False(historyExists, "TaskStatusHistory record must not exist after rollback.");
    }

    [Fact]
    public async Task DepartmentStaffOutsideAcademicScope_ReturnsFalse_UsingSqlServer()
    {
        using var context = _fixture.CreateContext();

        // 1. Department SE (id from seed)
        var seDeptId = await context.Departments.Where(d => d.Code == "SE").Select(d => d.Id).FirstAsync();

        // 2. Insert another Department (AI) and Major (AI_MAJ)
        var orgId = await context.Organizations.Select(o => o.Id).FirstAsync();
        var aiDept = new Department { OrganizationId = orgId, Code = "AI_" + Guid.NewGuid().ToString("N")[..4], Name = "AI Dept", IsActive = true };
        context.Departments.Add(aiDept);
        await context.SaveChangesAsync();

        var aiMajor = new Major { DepartmentId = aiDept.Id, Code = "AIM_" + Guid.NewGuid().ToString("N")[..4], Name = "AI Major", IsActive = true };
        context.Majors.Add(aiMajor);
        await context.SaveChangesAsync();

        // 3. Insert staff user belonging to AI department
        var staffUser = new User
        {
            DepartmentId = aiDept.Id,
            Email = "ai_staff_" + Guid.NewGuid().ToString("N")[..6] + "@aipms.test",
            FullName = "AI Staff",
            PasswordHash = "HASH",
            Status = "ACTIVE"
        };
        context.Users.Add(staffUser);
        await context.SaveChangesAsync();

        var staffRoleId = await context.Roles.Where(r => r.Code == "DEPARTMENT_STAFF").Select(r => r.Id).FirstAsync();
        context.UserRoles.Add(new UserRole { UserId = staffUser.Id, RoleId = staffRoleId });
        await context.SaveChangesAsync();

        // 4. Create Project with major in SE department
        var seMajorId = await context.Majors.Where(m => m.DepartmentId == seDeptId).Select(m => m.Id).FirstAsync();
        var (seProjectId, _, _) = await SeedProjectAndMilestoneAsync(context);
        context.ProjectMajors.Add(new ProjectMajor { ProjectId = seProjectId, MajorId = seMajorId });
        await context.SaveChangesAsync();

        // 5. Verify using real ProjectAccessService with SQL Server
        var accessService = new ProjectAccessService(context);

        var canAccessOutside = await accessService.CanAccessAsync(staffUser.Id, seProjectId, CancellationToken.None);
        Assert.False(canAccessOutside, "Staff outside academic scope must be denied access.");

        // 6. Associate an AI project and verify inside academic scope is allowed
        var (aiProjectId, _, _) = await SeedProjectAndMilestoneAsync(context);
        context.ProjectMajors.Add(new ProjectMajor { ProjectId = aiProjectId, MajorId = aiMajor.Id });
        await context.SaveChangesAsync();

        var canAccessInside = await accessService.CanAccessAsync(staffUser.Id, aiProjectId, CancellationToken.None);
        Assert.True(canAccessInside, "Staff inside academic scope must be granted access.");
    }

    [Fact]
    public async Task FormerTeamMember_IsNotActive_UsingSqlServer()
    {
        using var context = _fixture.CreateContext();
        var semId = await context.AcademicSemesters.Select(s => s.Id).FirstAsync();

        // 1. Create a former user who left team
        var user = new User
        {
            Email = "former_" + Guid.NewGuid().ToString("N")[..6] + "@aipms.test",
            FullName = "Former Member",
            PasswordHash = "HASH",
            Status = "ACTIVE"
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var team = new Team
        {
            AcademicSemesterId = semId,
            Code = "TM_" + Guid.NewGuid().ToString("N")[..6],
            Name = "Team Former Test",
            Status = "ELIGIBLE",
            CreatedBy = user.Id
        };
        context.Teams.Add(team);
        await context.SaveChangesAsync();

        // Add user with LeftAt != null
        var member = new TeamMember
        {
            TeamId = team.Id,
            AcademicSemesterId = semId,
            UserId = user.Id,
            IsLeader = false,
            JoinedAt = DateTime.UtcNow.AddDays(-10),
            LeftAt = DateTime.UtcNow.AddDays(-2)
        };
        context.TeamMembers.Add(member);

        var project = new Project
        {
            TeamId = team.Id,
            Code = "PRJ_" + Guid.NewGuid().ToString("N")[..6],
            Title = "Former Member Project",
            Status = "ACTIVE",
            CreatedBy = user.Id,
            RowVersion = new byte[] { 1 }
        };
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        // 2. Verify with real TaskRepository in SQL Server
        var repo = new TaskRepository(context);
        var isActive = await repo.IsUserActiveTeamMemberAsync(project.Id, user.Id, CancellationToken.None);

        Assert.False(isActive, "Former team member with LeftAt set must not be considered active.");
    }

    [Fact]
    public async Task GetTasksAsync_PaginationMetadata_IsCorrect_UsingSqlServer()
    {
        using var context = _fixture.CreateContext();
        var (projectId, milestoneId, userId) = await SeedProjectAndMilestoneAsync(context);

        // Seed 3 tasks in SQL Server
        context.Tasks.AddRange(
            new TaskEntity { MilestoneId = milestoneId, Title = "Task A", Status = "TODO", Priority = "MEDIUM", CreatedBy = userId },
            new TaskEntity { MilestoneId = milestoneId, Title = "Task B", Status = "TODO", Priority = "MEDIUM", CreatedBy = userId },
            new TaskEntity { MilestoneId = milestoneId, Title = "Task C", Status = "TODO", Priority = "MEDIUM", CreatedBy = userId }
        );
        await context.SaveChangesAsync();

        var repo = new TaskRepository(context);

        // Request Page = 2, PageSize = 1
        var pageResult = await repo.GetTasksAsync(
            projectId: projectId,
            milestoneId: null,
            status: null,
            priority: null,
            assigneeUserId: null,
            search: null,
            dueFrom: null,
            dueTo: null,
            isOverdue: null,
            isBlocked: null,
            page: 2,
            pageSize: 1,
            cancellationToken: CancellationToken.None);

        Assert.Equal(2, pageResult.Page);
        Assert.Equal(1, pageResult.PageSize);
        Assert.Equal(3, pageResult.TotalCount);
        Assert.Equal(3, pageResult.TotalPages);
        Assert.Single(pageResult.Items);

        // Request non-existent project (empty pagination)
        var emptyResult = await repo.GetTasksAsync(
            projectId: 9999999L,
            milestoneId: null,
            status: null,
            priority: null,
            assigneeUserId: null,
            search: null,
            dueFrom: null,
            dueTo: null,
            isOverdue: null,
            isBlocked: null,
            page: 1,
            pageSize: 10,
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, emptyResult.Page);
        Assert.Equal(10, emptyResult.PageSize);
        Assert.Equal(0, emptyResult.TotalCount);
        Assert.Equal(0, emptyResult.TotalPages);
        Assert.Empty(emptyResult.Items);
    }
}
