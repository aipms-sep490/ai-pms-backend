using AIPMS.Domain.Entities;
using AIPMS.Domain.Enums;
using AIPMS.Domain.Exceptions;

namespace AIPMS.UnitTests.Domain;

public sealed class ProjectStateMachineTests
{
    [Fact]
    public void TransitionTo_WhenTransitionIsAllowed_UpdatesStatus()
    {
        var project = new Project(Guid.NewGuid(), "AI-PMS");

        project.TransitionTo(ProjectStatus.Submitted);

        Assert.Equal(ProjectStatus.Submitted, project.Status);
    }

    [Fact]
    public void TransitionTo_WhenTransitionIsNotAllowed_ThrowsDomainException()
    {
        var project = new Project(Guid.NewGuid(), "AI-PMS");

        var action = () => project.TransitionTo(ProjectStatus.Active);

        Assert.Throws<DomainException>(action);
    }
}
