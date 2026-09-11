namespace AIPMS.Application.Abstractions.Storage;

// Keys are server-generated opaque identifiers; callers never supply filesystem paths.
public interface IFileStorage
{
    // Create only: never overwrite. On failure the provider cleans its partial write;
    // callers own cleanup only after success, so key collisions cannot delete existing content.
    Task WriteAsync(string key, Stream content, CancellationToken ct);
    Task<Stream> OpenReadAsync(string key, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}
