using AIPMS.Application.Features.Tasks.Domain;
using Xunit;

namespace AIPMS.UnitTests.Domain;

public sealed class TaskStateMachineTests
{
    [Theory]
    [InlineData("TODO", "IN_PROGRESS", true)]
    [InlineData("TODO", "BLOCKED", true)]
    [InlineData("TODO", "CANCELLED", true)]
    [InlineData("TODO", "DONE", false)]
    [InlineData("IN_PROGRESS", "TODO", true)]
    [InlineData("IN_PROGRESS", "BLOCKED", true)]
    [InlineData("IN_PROGRESS", "IN_REVIEW", true)]
    [InlineData("IN_PROGRESS", "DONE", true)]
    [InlineData("IN_PROGRESS", "CANCELLED", true)]
    [InlineData("BLOCKED", "TODO", true)]
    [InlineData("BLOCKED", "IN_PROGRESS", true)]
    [InlineData("BLOCKED", "CANCELLED", true)]
    [InlineData("BLOCKED", "DONE", false)]
    [InlineData("IN_REVIEW", "IN_PROGRESS", true)]
    [InlineData("IN_REVIEW", "DONE", true)]
    [InlineData("IN_REVIEW", "CANCELLED", true)]
    [InlineData("DONE", "IN_PROGRESS", true)]
    [InlineData("DONE", "CANCELLED", true)]
    [InlineData("DONE", "TODO", false)]
    [InlineData("CANCELLED", "TODO", true)]
    [InlineData("CANCELLED", "IN_PROGRESS", false)]
    [InlineData("CANCELLED", "DONE", false)]
    public void CanTransition_ShouldEnforceStateMachineRules(string current, string next, bool expected)
    {
        var result = TaskStateMachine.CanTransition(current, next);
        Assert.Equal(expected, result);
    }
}
