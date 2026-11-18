namespace AIPMS.Application.Features.Files.Abstractions;

public interface IProjectFileUploadSessions
{
    Task<ProjectFileUploadSession> CreateAsync(long actorId, CreateProjectFileUploadSession request, CancellationToken ct);
    Task<ProjectFileUploadSession> GetAsync(long actorId, string sessionId, CancellationToken ct);
    Task<ProjectFileUploadSession> AppendAsync(long actorId, string sessionId, long offset, Stream content, long length, CancellationToken ct);
    Task<ProjectFileUploadSession> CompleteAsync(long actorId, string sessionId, CancellationToken ct);
    Task CancelAsync(long actorId, string sessionId, CancellationToken ct);
}
