using System;
using System.Collections.Generic;
using AIPMS.Application.Features.Tasks.Commands;
using AIPMS.Application.Features.Tasks.Queries;
using FluentValidation.TestHelper;
using Xunit;

namespace AIPMS.UnitTests.Application;

public sealed class TaskValidatorTests
{
    // ── CreateTaskCommandValidator ──────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void CreateTask_EmptyOrWhitespaceTitle_ShouldFail(string? title)
    {
        var validator = new CreateTaskCommandValidator();
        var cmd = MakeCreate(title: title);
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void CreateTask_TitleExceeds255Chars_ShouldFail()
    {
        var validator = new CreateTaskCommandValidator();
        var cmd = MakeCreate(title: new string('T', 256));
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void CreateTask_ZeroMilestoneId_ShouldFail()
    {
        var validator = new CreateTaskCommandValidator();
        var cmd = MakeCreate(milestoneId: 0);
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.MilestoneId);
    }

    [Fact]
    public void CreateTask_DueAtBeforeStartAt_ShouldFail()
    {
        var validator = new CreateTaskCommandValidator();
        var now = DateTime.UtcNow;
        var cmd = MakeCreate(startAt: now, dueAt: now.AddDays(-1));
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor("");
    }

    [Fact]
    public void CreateTask_DuplicateAssignees_ShouldFail()
    {
        var validator = new CreateTaskCommandValidator();
        var cmd = MakeCreate(assigneeUserIds: [10, 10]);
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.AssigneeUserIds);
    }

    [Fact]
    public void CreateTask_ValidCommand_ShouldPass()
    {
        var validator = new CreateTaskCommandValidator();
        var now = DateTime.UtcNow;
        var cmd = MakeCreate(startAt: now, dueAt: now.AddDays(5), assigneeUserIds: [10, 11]);
        var result = validator.TestValidate(cmd);
        result.ShouldNotHaveAnyValidationErrors();
    }

    // ── UpdateTaskCommandValidator ──────────────────────────────────────────

    [Fact]
    public void UpdateTask_ZeroId_ShouldFail()
    {
        var validator = new UpdateTaskCommandValidator();
        var cmd = MakeUpdate(id: 0);
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Id);
    }

    // ── UpdateTaskStatusCommandValidator ────────────────────────────────────

    [Fact]
    public void UpdateTaskStatus_ZeroId_ShouldFail()
    {
        var validator = new UpdateTaskStatusCommandValidator();
        var cmd = new UpdateTaskStatusCommand(0, "IN_PROGRESS", null);
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.TaskId);
    }

    [Fact]
    public void UpdateTaskStatus_BlockedWithoutReason_ShouldFail()
    {
        var validator = new UpdateTaskStatusCommandValidator();
        var cmd = new UpdateTaskStatusCommand(1, "BLOCKED", null);
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Fact]
    public void UpdateTaskStatus_BlockedWithWhitespaceReason_ShouldFail()
    {
        var validator = new UpdateTaskStatusCommandValidator();
        var cmd = new UpdateTaskStatusCommand(1, "BLOCKED", "   ");
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Fact]
    public void UpdateTaskStatus_BlockedWithValidReason_ShouldPass()
    {
        var validator = new UpdateTaskStatusCommandValidator();
        var cmd = new UpdateTaskStatusCommand(1, "BLOCKED", "Waiting for API specs");
        var result = validator.TestValidate(cmd);
        result.ShouldNotHaveAnyValidationErrors();
    }

    // ── SetTaskAssigneesCommandValidator ────────────────────────────────────

    [Fact]
    public void SetTaskAssignees_DuplicateAssignees_ShouldFail()
    {
        var validator = new SetTaskAssigneesCommandValidator();
        var cmd = new SetTaskAssigneesCommand(1, [10, 10]);
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.AssigneeUserIds);
    }

    // ── AddTaskDependencyCommandValidator ───────────────────────────────────

    [Fact]
    public void AddDependency_SelfDependency_ShouldFail()
    {
        var validator = new AddTaskDependencyCommandValidator();
        var cmd = new AddTaskDependencyCommand(5, 5, "FINISH_TO_START");
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor("");
    }

    [Fact]
    public void AddDependency_ValidIds_ShouldPass()
    {
        var validator = new AddTaskDependencyCommandValidator();
        var cmd = new AddTaskDependencyCommand(5, 10, "FINISH_TO_START");
        var result = validator.TestValidate(cmd);
        result.ShouldNotHaveAnyValidationErrors();
    }

    // ── Queries Validators ──────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GetTasks_InvalidPage_ShouldFail(int page)
    {
        var validator = new GetTasksQueryValidator();
        var query = new GetTasksQuery(1, null, null, null, null, null, null, null, null, null, page, 10);
        var result = validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.Page);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void GetTasks_InvalidPageSize_ShouldFail(int pageSize)
    {
        var validator = new GetTasksQueryValidator();
        var query = new GetTasksQuery(1, null, null, null, null, null, null, null, null, null, 1, pageSize);
        var result = validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.PageSize);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static CreateTaskCommand MakeCreate(
        long milestoneId = 1,
        long? parentTaskId = null,
        string? title = "Task Title",
        DateTime? startAt = null,
        DateTime? dueAt = null,
        IReadOnlyList<long>? assigneeUserIds = null) =>
        new(
            milestoneId,
            parentTaskId,
            title!,
            "Task Description",
            "MEDIUM",
            startAt,
            dueAt,
            assigneeUserIds ?? Array.Empty<long>());

    private static UpdateTaskCommand MakeUpdate(
        long id = 1,
        long milestoneId = 1,
        long? parentTaskId = null,
        string? title = "Updated Task Title") =>
        new(
            id,
            milestoneId,
            parentTaskId,
            title ?? "Updated Task Title",
            "Description",
            "HIGH",
            null,
            null);
}
