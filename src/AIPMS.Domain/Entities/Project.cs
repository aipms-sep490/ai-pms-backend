using AIPMS.Domain.Enums;
using AIPMS.Domain.Exceptions;

namespace AIPMS.Domain.Entities;

public sealed class Project
{
    private Project()
    {
    }

    public Project(Guid id, string name)
    {
        if (id == Guid.Empty)
        {
            throw new DomainException("Project id cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Project name is required.");
        }

        Id = id;
        Name = name.Trim();
        Status = ProjectStatus.Draft;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public ProjectStatus Status { get; private set; }

    public void TransitionTo(ProjectStatus nextStatus)
    {
        if (!ProjectStateMachine.CanTransition(Status, nextStatus))
        {
            throw new DomainException($"Cannot transition project from {Status} to {nextStatus}.");
        }

        Status = nextStatus;
    }
}
