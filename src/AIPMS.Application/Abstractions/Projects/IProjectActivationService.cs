namespace AIPMS.Application.Abstractions.Projects;

public interface IProjectActivationService
{
    Task ApplyMilestoneTemplateAsync(long projectId, long actorUserId, DateTime activatedAt, CancellationToken cancellationToken = default);
}
