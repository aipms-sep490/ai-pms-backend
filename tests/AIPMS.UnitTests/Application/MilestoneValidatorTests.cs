using System;
using AIPMS.Application.Features.Milestones.Commands;
using AIPMS.Application.Features.Milestones.Queries;
using FluentValidation.TestHelper;
using Xunit;

namespace AIPMS.UnitTests.Application;

public sealed class MilestoneValidatorTests
{
    // ── CreateMilestoneCommandValidator ─────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void CreateMilestone_EmptyOrWhitespaceTitle_ShouldFail(string? title)
    {
        var validator = new CreateMilestoneCommandValidator();
        var cmd = new CreateMilestoneCommand(1, title!, "Desc", DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)), 0);
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void CreateMilestone_TitleExceeds200Chars_ShouldFail()
    {
        var validator = new CreateMilestoneCommandValidator();
        var cmd = new CreateMilestoneCommand(1, new string('A', 256), "Desc", DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)), 0);
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void CreateMilestone_ZeroProjectId_ShouldFail()
    {
        var validator = new CreateMilestoneCommandValidator();
        var cmd = new CreateMilestoneCommand(0, "Title", "Desc", DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)), 0);
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.ProjectId);
    }

    [Fact]
    public void CreateMilestone_NegativeSortOrder_ShouldFail()
    {
        var validator = new CreateMilestoneCommandValidator();
        var cmd = new CreateMilestoneCommand(1, "Title", "Desc", DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)), -1);
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.SortOrder);
    }

    [Fact]
    public void CreateMilestone_DueDateBeforeStartDate_ShouldFail()
    {
        var validator = new CreateMilestoneCommandValidator();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var cmd = new CreateMilestoneCommand(1, "Title", "Desc", today, today.AddDays(-1), 0);
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor("");
    }

    [Fact]
    public void CreateMilestone_ValidCommand_ShouldPass()
    {
        var validator = new CreateMilestoneCommandValidator();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var cmd = new CreateMilestoneCommand(1, "Sprint 1", "Initial Sprint", today, today.AddDays(14), 0);
        var result = validator.TestValidate(cmd);
        result.ShouldNotHaveAnyValidationErrors();
    }

    // ── UpdateMilestoneCommandValidator ─────────────────────────────────────

    [Fact]
    public void UpdateMilestone_ZeroId_ShouldFail()
    {
        var validator = new UpdateMilestoneCommandValidator();
        var cmd = new UpdateMilestoneCommand(0, "Title", "Desc", null, null, "IN_PROGRESS", 0);
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Id);
    }

    [Fact]
    public void UpdateMilestone_InvalidStatus_ShouldFail()
    {
        var validator = new UpdateMilestoneCommandValidator();
        var cmd = new UpdateMilestoneCommand(1, "Title", "Desc", null, null, "INVALID_STATUS", 0);
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Status);
    }

    // ── ReorderMilestonesCommandValidator ────────────────────────────────────

    [Fact]
    public void ReorderMilestones_ZeroProjectId_ShouldFail()
    {
        var validator = new ReorderMilestonesCommandValidator();
        var cmd = new ReorderMilestonesCommand(0, new[] { new MilestoneReorderItem(1, 0) });
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.ProjectId);
    }

    [Fact]
    public void ReorderMilestones_EmptyItems_ShouldFail()
    {
        var validator = new ReorderMilestonesCommandValidator();
        var cmd = new ReorderMilestonesCommand(1, Array.Empty<MilestoneReorderItem>());
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Items);
    }

    [Fact]
    public void ReorderMilestones_DuplicateMilestoneIds_ShouldFail()
    {
        var validator = new ReorderMilestonesCommandValidator();
        var items = new[] { new MilestoneReorderItem(1, 0), new MilestoneReorderItem(1, 1) };
        var cmd = new ReorderMilestonesCommand(1, items);
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Items);
    }

    [Fact]
    public void ReorderMilestones_NegativeSortOrder_ShouldFail()
    {
        var validator = new ReorderMilestonesCommandValidator();
        var items = new[] { new MilestoneReorderItem(1, -1), new MilestoneReorderItem(2, 0) };
        var cmd = new ReorderMilestonesCommand(1, items);
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Items);
    }

    [Fact]
    public void ReorderMilestones_DuplicateSortOrders_ShouldFail()
    {
        var validator = new ReorderMilestonesCommandValidator();
        var items = new[] { new MilestoneReorderItem(1, 0), new MilestoneReorderItem(2, 0) };
        var cmd = new ReorderMilestonesCommand(1, items);
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Items);
    }

    [Fact]
    public void ReorderMilestones_ValidItems_ShouldPass()
    {
        var validator = new ReorderMilestonesCommandValidator();
        var items = new[] { new MilestoneReorderItem(1, 0), new MilestoneReorderItem(2, 1) };
        var cmd = new ReorderMilestonesCommand(1, items);
        var result = validator.TestValidate(cmd);
        result.ShouldNotHaveAnyValidationErrors();
    }

    // ── DeleteMilestoneCommandValidator ─────────────────────────────────────

    [Fact]
    public void DeleteMilestone_ZeroId_ShouldFail()
    {
        var validator = new DeleteMilestoneCommandValidator();
        var result = validator.TestValidate(new DeleteMilestoneCommand(0));
        result.ShouldHaveValidationErrorFor(x => x.Id);
    }

    // ── Queries Validators ──────────────────────────────────────────────────

    [Fact]
    public void GetMilestones_ZeroProjectId_ShouldFail()
    {
        var validator = new GetMilestonesQueryValidator();
        var result = validator.TestValidate(new GetMilestonesQuery(0));
        result.ShouldHaveValidationErrorFor(x => x.ProjectId);
    }

    [Fact]
    public void GetMilestoneById_ZeroId_ShouldFail()
    {
        var validator = new GetMilestoneByIdQueryValidator();
        var result = validator.TestValidate(new GetMilestoneByIdQuery(0));
        result.ShouldHaveValidationErrorFor(x => x.Id);
    }
}
