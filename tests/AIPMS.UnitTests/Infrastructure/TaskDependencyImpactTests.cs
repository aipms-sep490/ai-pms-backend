using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Tasks.Commands;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Repositories;
using AIPMS.Infrastructure.Services.Projects;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.UnitTests.Infrastructure;

public sealed class TaskDependencyImpactTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task Reopen_preserves_completed_successor_and_creates_durable_impact_and_notification()
    {
        using var db = await Seed();
        var originalCompletion = db.Tasks.Single(t => t.Id == 2).CompletedAt;
        var originalToken = db.Tasks.Single(t => t.Id == 2).ConcurrencyToken;
        await new TaskRepository(db).UpdateStatusAsync(1, "IN_PROGRESS", "Change schema", 1, Ct);

        var impact = Assert.Single(await db.TaskDependencyImpacts.ToArrayAsync());
        Assert.Equal(2, impact.TaskId);
        Assert.Equal(1, impact.PredecessorTaskId);
        Assert.Equal("PENDING", impact.Status);
        Assert.True(impact.ReopenHistoryId > 0);
        Assert.Equal("DONE", db.Tasks.Single(t => t.Id == 2).Status);
        Assert.Equal(originalCompletion, db.Tasks.Single(t => t.Id == 2).CompletedAt);
        Assert.NotEqual(originalToken, db.Tasks.Single(t => t.Id == 2).ConcurrencyToken);
        Assert.Equal("DONE", db.Tasks.Single(t => t.Id == 3).Status);
        var notification = Assert.Single(await db.Notifications.Include(x => x.NotificationRecipients).ToArrayAsync());
        Assert.Equal("TASK_DEPENDENCY_IMPACT", notification.NotificationType);
        Assert.Contains(notification.NotificationRecipients, x => x.UserId == 1);
        Assert.Contains(notification.NotificationRecipients, x => x.UserId == 2);
    }

    [Fact]
    public async Task Completing_predecessor_does_not_silently_clear_impact()
    {
        using var db = await Seed();
        var repository = new TaskRepository(db);
        await repository.UpdateStatusAsync(1, "IN_PROGRESS", "Change schema", 1, Ct);
        await repository.UpdateStatusAsync(1, "DONE", null, 1, Ct);
        db.ChangeTracker.Clear();
        var result = await Service(db).GetAsync(2, Ct);
        var impact = Assert.Single(result.Items);
        Assert.Equal("PENDING", impact.Status);
        Assert.True(impact.CanResolve);
        Assert.True(result.CanReview);
    }

    [Theory]
    [InlineData("CONFIRMED_VALID", "DONE")]
    [InlineData("REWORK_REQUIRED", "IN_PROGRESS")]
    public async Task Cancelled_predecessor_impact_can_be_explicitly_resolved(string decision, string expectedStatus)
    {
        using var db = await Seed();
        await new TaskRepository(db).UpdateStatusAsync(1, "CANCELLED", "Superseded schema", 1, Ct);
        var impact = Assert.Single((await Service(db).GetAsync(2, Ct)).Items);
        Assert.True(impact.CanResolve);
        var result = await Service(db).ResolveAsync(2, impact.Id, decision, "Reviewed cancellation impact", Ct);
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(decision, (await db.TaskDependencyImpacts.FindAsync(impact.Id))!.Status);
    }

    [Fact]
    public async Task Confirming_valid_keeps_completion_and_records_reviewer_reason_and_audit()
    {
        using var db = await Seed();
        var id = await ReopenAndComplete(db);
        var audit = new Audit();
        var completion = db.Tasks.Single(t => t.Id == 2).CompletedAt;
        var result = await Service(db, audit: audit).ResolveAsync(2, id, "CONFIRMED_VALID", "No SQL changes needed", Ct);
        Assert.Equal("DONE", result.Status);
        Assert.Equal(completion, result.CompletedAt);
        var impact = await db.TaskDependencyImpacts.SingleAsync();
        Assert.Equal("CONFIRMED_VALID", impact.Status);
        Assert.Equal(1, impact.ReviewedBy);
        Assert.Equal("No SQL changes needed", impact.ReviewReason);
        Assert.NotNull(impact.ReviewedAt);
        Assert.Equal("TASK_DEPENDENCY_IMPACT_RESOLVED", Assert.Single(audit.Entries).Action);
        await Assert.ThrowsAsync<ConflictException>(() => Service(db).ResolveAsync(2, id, "REWORK_REQUIRED", "Duplicate decision", Ct));
    }

    [Fact]
    public async Task Explicit_rework_reopens_only_reviewed_task_and_marks_its_completed_successor()
    {
        using var db = await Seed();
        var id = await ReopenAndComplete(db);
        var result = await Service(db).ResolveAsync(2, id, "REWORK_REQUIRED", "Update SQL to match schema", Ct);
        Assert.Equal("IN_PROGRESS", result.Status);
        Assert.Null(result.CompletedAt);
        Assert.Equal("DONE", db.Tasks.Single(t => t.Id == 3).Status);
        Assert.Equal("REWORK_REQUIRED", (await db.TaskDependencyImpacts.FindAsync(id))!.Status);
        var next = Assert.Single(await db.TaskDependencyImpacts.Where(x => x.TaskId == 3).ToArrayAsync());
        Assert.Equal("PENDING", next.Status);
        Assert.Equal(2, next.PredecessorTaskId);
        Assert.Contains(await db.TaskStatusHistories.ToArrayAsync(), h => h.TaskId == 2 && h.OldStatus == "DONE" && h.NewStatus == "IN_PROGRESS");
    }

    [Theory]
    [InlineData("CONFIRMED_VALID")]
    [InlineData("REWORK_REQUIRED")]
    public async Task Cannot_resolve_while_predecessor_is_unfinished(string decision)
    {
        using var db = await Seed();
        await new TaskRepository(db).UpdateStatusAsync(1, "IN_PROGRESS", "Change schema", 1, Ct);
        var impact = await db.TaskDependencyImpacts.SingleAsync();
        Assert.False(Assert.Single((await Service(db).GetAsync(2, Ct)).Items).CanResolve);
        await Assert.ThrowsAsync<ConflictException>(() => Service(db).ResolveAsync(2, impact.Id, decision, "Checked", Ct));
        Assert.Equal("PENDING", impact.Status);
        Assert.Equal("DONE", db.Tasks.Single(t => t.Id == 2).Status);
    }

    [Fact]
    public async Task Member_can_read_but_cannot_resolve_and_outsider_cannot_read()
    {
        using var db = await Seed();
        var id = await ReopenAndComplete(db);
        Assert.False((await Service(db, actor: 2).GetAsync(2, Ct)).CanReview);
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(db, actor: 2).ResolveAsync(2, id, "CONFIRMED_VALID", "Checked", Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(db, access: false).GetAsync(2, Ct));
    }

    [Fact]
    public async Task Closed_project_disables_review_and_rejects_resolution()
    {
        using var db = await Seed();
        var id = await ReopenAndComplete(db);
        db.Projects.Single().Status = "COMPLETED";
        await db.SaveChangesAsync();
        Assert.False((await Service(db).GetAsync(2, Ct)).CanReview);
        await Assert.ThrowsAsync<ConflictException>(() => Service(db).ResolveAsync(2, id, "CONFIRMED_VALID", "Checked", Ct));
    }

    [Fact]
    public async Task A_second_reopen_creates_a_new_record_without_rewriting_the_old_review()
    {
        using var db = await Seed();
        var id = await ReopenAndComplete(db);
        await Service(db).ResolveAsync(2, id, "CONFIRMED_VALID", "First checked", Ct);
        await new TaskRepository(db).UpdateStatusAsync(1, "IN_PROGRESS", "Second change", 1, Ct);
        var items = (await Service(db).GetAsync(2, Ct)).Items;
        Assert.Equal(2, items.Count);
        Assert.Contains(items, x => x.Status == "CONFIRMED_VALID" && x.ReviewReason == "First checked");
        Assert.Contains(items, x => x.Status == "PENDING" && x.ReopenReason == "Second change");
    }

    [Fact]
    public async Task Pending_impact_cannot_be_bypassed_by_reopening_and_completing_the_task()
    {
        using var db = await Seed();
        await ReopenAndComplete(db);
        var repository = new TaskRepository(db);
        await repository.UpdateStatusAsync(2, "IN_PROGRESS", "Independent rework", 1, Ct);
        var error = await Assert.ThrowsAsync<ConflictException>(() => repository.UpdateStatusAsync(2, "DONE", null, 1, Ct));
        Assert.Equal(WorkflowErrorCodes.TaskDependencyImpactPending, error.Code);
        Assert.Equal("IN_PROGRESS", db.Tasks.Single(t => t.Id == 2).Status);
    }

    [Theory]
    [InlineData("IN_REVIEW")]
    [InlineData("DONE")]
    [InlineData("CANCELLED")]
    public async Task Locked_assignments_are_rejected_by_handler_and_repository(string status)
    {
        using var db = await Seed();
        db.Tasks.Single(t => t.Id == 2).Status = status;
        await db.SaveChangesAsync();
        var repository = new TaskRepository(db);
        var handler = new SetTaskAssigneesCommandHandler(repository, new MilestoneRepository(db), new ProjectExecutionGuard(db), new User(1), new Audit());
        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(new(2, [1, 2]), Ct));
        await Assert.ThrowsAsync<ConflictException>(() => repository.SetAssigneesAsync(2, [1, 2], 1, Ct));
        Assert.Equal(new long[] { 2 }, await db.TaskAssignees.Where(x => x.TaskId == 2).Select(x => x.UserId).ToArrayAsync());
    }

    [Fact]
    public async Task Reopen_requires_manager_and_reason_and_completion_audit_keeps_assignee_snapshot()
    {
        using var db = await Seed();
        var repository = new TaskRepository(db);
        var audit = new Audit();
        var guard = new ProjectExecutionGuard(db);
        var member = new UpdateTaskStatusCommandHandler(repository, new MilestoneRepository(db), guard, new User(2), audit);
        await Assert.ThrowsAsync<ForbiddenException>(() => member.Handle(new(2, "IN_PROGRESS", "Need edits"), Ct));
        var manager = new UpdateTaskStatusCommandHandler(repository, new MilestoneRepository(db), guard, new User(1), audit);
        await Assert.ThrowsAsync<ConflictException>(() => manager.Handle(new(1, "IN_PROGRESS", " "), Ct));
        await manager.Handle(new(1, "IN_PROGRESS", "Need edits"), Ct);
        await manager.Handle(new(1, "DONE", null), Ct);
        var doneAudit = audit.Entries.Last();
        Assert.Equal(new long[] { 1 }, Assert.IsType<long[]>(doneAudit.Context["assigneeUserIds"]));
    }

    [Fact]
    public async Task Assignment_audit_retains_before_and_after_ids()
    {
        using var db = await Seed();
        db.Tasks.Single(t => t.Id == 2).Status = "IN_PROGRESS";
        await db.SaveChangesAsync();
        var audit = new Audit();
        var handler = new SetTaskAssigneesCommandHandler(new TaskRepository(db), new MilestoneRepository(db), new ProjectExecutionGuard(db), new User(1), audit);
        await handler.Handle(new(2, [1, 2]), Ct);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(new long[] { 2 }, Assert.IsType<long[]>(entry.Context["oldAssigneeUserIds"]));
        Assert.Equal(new long[] { 1, 2 }, Assert.IsType<long[]>(entry.Context["newAssigneeUserIds"]));
    }

    [Fact]
    public async Task Same_done_status_does_not_rewrite_completion_time_or_create_false_impact()
    {
        using var db = await Seed();
        var completion = db.Tasks.Single(t => t.Id == 1).CompletedAt;
        await new TaskRepository(db).UpdateStatusAsync(1, "DONE", null, 1, Ct);
        Assert.Equal(completion, db.Tasks.Single(t => t.Id == 1).CompletedAt);
        Assert.Empty(await db.TaskDependencyImpacts.ToArrayAsync());
        Assert.Empty(await db.TaskStatusHistories.ToArrayAsync());
    }

    private static TaskDependencyImpactService Service(AipmsDbContext db, long actor = 1, bool access = true, Audit? audit = null) =>
        new(db, new User(actor), new Access(access), new ProjectExecutionGuard(db), new TaskRepository(db), audit ?? new Audit(), TimeProvider.System);

    [Fact]
    public async Task Mentor_must_read_both_dependency_ends_and_never_receives_out_of_major_reopen_reason()
    {
        using var db = await Seed();
        await ReopenAndComplete(db);
        var org = new M.Organization { Id = 1, Code = "ORG", Name = "University", IsActive = true };
        var department = new M.Department { Id = 1, Organization = org, Code = "IT", Name = "IT", IsActive = true };
        db.Departments.Add(department);
        db.AcademicSemesters.Add(new M.AcademicSemester { Id = 1, Organization = org, Code = "SEM", Name = "Semester", Status = "ACTIVE" });
        db.Majors.AddRange(new M.Major { Id = 1, Department = department, Code = "SE", Name = "SE", IsActive = true },
            new M.Major { Id = 2, Department = department, Code = "BA", Name = "BA", IsActive = true });
        db.Roles.Add(new M.Role { Id = 2, Code = "LECTURER", Name = "Lecturer" });
        var mentor = new M.User { Id = 3, Email = "mentor@example.test", FullName = "Mentor", PasswordHash = "test", Status = "ACTIVE", Department = department,
            UserRoleUsers = [new M.UserRole { RoleId = 2 }] };
        db.SupervisorAssignments.Add(new M.SupervisorAssignment { ProjectId = 1, AssignmentType = "DISCIPLINE_MENTOR", MajorId = 1,
            SupervisorProfile = new M.SupervisorProfile { User = mentor, IsAvailable = true } });
        db.TaskDisciplines.AddRange(new M.TaskDiscipline { TaskId = 1, MajorId = 2, Role = "PRIMARY", CreatedBy = 1 },
            new M.TaskDiscipline { TaskId = 2, MajorId = 1, Role = "PRIMARY", CreatedBy = 1 });
        var evidence = new AIPMS.Application.Features.Projects.DTOs.RegistrationEvidence(
            new("INTERDISCIPLINARY", null, 1, [new(1, 1, 2, "Software"), new(2, 1, 2, "Business")], Guid.NewGuid()),
            new(2, 4, 2, "test"), 1, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), [], [1],
            MajorDepartmentIds: new Dictionary<long, long> { [1] = 1, [2] = 1 });
        db.Set<AIPMS.Infrastructure.Persistence.Models.ProjectRegistrationSnapshot>().Add(new()
        { ProjectId = 1, ProjectPeriodId = 1, LeadDepartmentId = 1, SubmittedBy = 1, SnapshotJson = System.Text.Json.JsonSerializer.Serialize(evidence) });
        await db.SaveChangesAsync();
        var service = Service(db, actor: 3);
        Assert.Empty((await service.GetAsync(2, Ct)).Items);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetAsync(1, Ct));
        db.TaskDisciplines.Add(new M.TaskDiscipline { TaskId = 1, MajorId = 1, Role = "SUPPORTING", CreatedBy = 1 });
        await db.SaveChangesAsync();
        var allowed = await service.GetAsync(2, Ct);
        Assert.Single(allowed.Items);
        Assert.False(allowed.CanReview);
    }

    [Fact]
    public async Task Restoring_cancelled_completed_work_requires_manager_reason_and_creates_fresh_review()
    {
        using var db = await Seed();
        db.TaskAssignees.Add(new M.TaskAssignee { TaskId = 1, UserId = 2, AssignedBy = 1 });
        db.Tasks.Single(t => t.Id == 4).Status = "CANCELLED";
        await db.SaveChangesAsync();
        var repository = new TaskRepository(db);
        await repository.UpdateStatusAsync(1, "CANCELLED", "Superseded", 1, Ct);
        var oldImpact = Assert.Single((await Service(db).GetAsync(2, Ct)).Items);
        await Service(db).ResolveAsync(2, oldImpact.Id, "CONFIRMED_VALID", "Cancellation accepted", Ct);
        var memberCapabilities = await new AIPMS.Infrastructure.Services.ExecutionCapabilities.ExecutionCapabilityService(
            db, new User(2), new Access(true), null!, repository).GetTaskAsync(1, Ct);
        var memberStatusAction = Assert.Single(memberCapabilities.Actions, x => x.Code == "change_task_status");
        Assert.False(memberStatusAction.Allowed);
        Assert.Contains("PROJECT_MANAGER_REQUIRED", memberStatusAction.Reasons);
        var managerCapabilities = await new AIPMS.Infrastructure.Services.ExecutionCapabilities.ExecutionCapabilityService(
            db, new User(1), new Access(true), null!, repository).GetTaskAsync(1, Ct);
        Assert.True(Assert.Single(managerCapabilities.Actions, x => x.Code == "change_task_status").Allowed);
        var guard = new ProjectExecutionGuard(db);
        var member = new UpdateTaskStatusCommandHandler(repository, new MilestoneRepository(db), guard, new User(2), new Audit());
        await Assert.ThrowsAsync<ForbiddenException>(() => member.Handle(new(1, "TODO", "Restore"), Ct));
        var manager = new UpdateTaskStatusCommandHandler(repository, new MilestoneRepository(db), guard, new User(1), new Audit());
        await Assert.ThrowsAsync<ConflictException>(() => manager.Handle(new(1, "TODO", " "), Ct));
        await manager.Handle(new(1, "TODO", "Restore original implementation"), Ct);
        var impacts = await db.TaskDependencyImpacts.OrderBy(x => x.Id).ToArrayAsync();
        Assert.Equal(2, impacts.Length);
        Assert.Equal("CONFIRMED_VALID", impacts[0].Status);
        Assert.Equal("PENDING", impacts[1].Status);
        Assert.All(impacts, x => Assert.Equal(2, x.TaskId));
        Assert.NotEqual(impacts[0].ReopenHistoryId, impacts[1].ReopenHistoryId);
        Assert.Equal("DONE", db.Tasks.Single(t => t.Id == 2).Status);
        Assert.Equal("CANCELLED", db.Tasks.Single(t => t.Id == 4).Status);
    }

    private static async Task<long> ReopenAndComplete(AipmsDbContext db)
    {
        var repository = new TaskRepository(db);
        await repository.UpdateStatusAsync(1, "IN_PROGRESS", "Change schema", 1, Ct);
        await repository.UpdateStatusAsync(1, "DONE", null, 1, Ct);
        return (await db.TaskDependencyImpacts.SingleAsync()).Id;
    }

    private static async Task<AipmsDbContext> Seed()
    {
        var db = new AipmsDbContext(new DbContextOptionsBuilder<AipmsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Roles.Add(new M.Role { Id = 1, Code = "STUDENT", Name = "Student" });
        for (var i = 1; i <= 2; i++)
        {
            db.Users.Add(new M.User { Id = i, Email = $"{i}@example.test", FullName = $"Student {i}", PasswordHash = "test", Status = "ACTIVE" });
            db.UserRoles.Add(new M.UserRole { Id = i, UserId = i, RoleId = 1 });
        }
        db.Teams.Add(new M.Team { Id = 1, AcademicSemesterId = 1, Code = "DEMO", Name = "Demo", Status = "ACTIVE", CreatedBy = 1 });
        db.TeamMembers.AddRange(new M.TeamMember { Id = 1, TeamId = 1, UserId = 1, IsLeader = true, AcademicSemesterId = 1 },
            new M.TeamMember { Id = 2, TeamId = 1, UserId = 2, AcademicSemesterId = 1 });
        db.Projects.Add(new M.Project { Id = 1, TeamId = 1, Code = "PRJ", Title = "Demo", Status = "ACTIVE", RowVersion = [1] });
        db.Milestones.Add(new M.Milestone { Id = 1, ProjectId = 1, Title = "Milestone", Status = "IN_PROGRESS", CreatedBy = 1 });
        for (var i = 1; i <= 4; i++)
            db.Tasks.Add(new M.Task { Id = i, MilestoneId = 1, Title = $"Task {i}", Status = i == 4 ? "TODO" : "DONE", CreatedBy = 1,
                CompletedAt = i == 4 ? null : DateTime.UtcNow.AddDays(-1), ConcurrencyToken = Guid.NewGuid() });
        db.TaskAssignees.AddRange(new M.TaskAssignee { Id = 1, TaskId = 1, UserId = 1, AssignedBy = 1 },
            new M.TaskAssignee { Id = 2, TaskId = 2, UserId = 2, AssignedBy = 1 });
        db.TaskDependencies.AddRange(new M.TaskDependency { Id = 1, TaskId = 2, DependsOnTaskId = 1, DependencyType = "FINISH_TO_START" },
            new M.TaskDependency { Id = 2, TaskId = 3, DependsOnTaskId = 2, DependencyType = "FINISH_TO_START" },
            new M.TaskDependency { Id = 3, TaskId = 4, DependsOnTaskId = 1, DependencyType = "FINISH_TO_START" });
        await db.SaveChangesAsync();
        return db;
    }

    private sealed class User(long id) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public long? UserId => id;
        public string? Email => null;
        public string? FullName => null;
        public IReadOnlyCollection<string> Roles => ["STUDENT"];
    }
    private sealed class Access(bool allowed) : IProjectAccessService
    {
        public Task<bool> CanAccessAsync(long userId, long projectId, CancellationToken cancellationToken = default) => Task.FromResult(allowed);
    }
    private sealed class Audit : IAuditTrail
    {
        public List<AuditEntry> Entries { get; } = [];
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        { Entries.Add(entry); return Task.CompletedTask; }
    }
}
