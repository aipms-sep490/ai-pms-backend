using System.IO;
using System.Threading.Tasks;

namespace AIPMS.Infrastructure.Storage;

internal interface IGoogleDriveClient
{
    Task<IReadOnlyList<string>> FindAsync(string folderId, string key, CancellationToken ct);
    Task UploadAsync(string folderId, string key, Stream content, CancellationToken ct);
    Task DownloadAsync(string id, Stream output, CancellationToken ct);
    Task DeleteAsync(string id, CancellationToken ct);
    Task CheckFolderAsync(string folderId, CancellationToken ct);
}
