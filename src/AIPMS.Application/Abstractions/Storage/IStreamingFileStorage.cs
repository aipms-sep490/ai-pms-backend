namespace AIPMS.Application.Abstractions.Storage;

public interface IStreamingFileStorage : IFileStorage
{
    // Forward-only download; the caller must dispose it to stop the remote transfer.
    Task<Stream> OpenSequentialReadAsync(string key, CancellationToken ct);
}
