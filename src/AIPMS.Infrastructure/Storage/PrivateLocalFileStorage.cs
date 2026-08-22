using AIPMS.Application.Abstractions.Storage;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AIPMS.Infrastructure.Storage;

internal sealed class PrivateLocalFileStorage : IFileStorage
{
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "App_Data", "private-files");

    public async Task StoreAsync(string storageKey, Stream content, CancellationToken cancellationToken)
    {
        var path = GetPath(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(destination, cancellationToken);
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        Stream result = new FileStream(GetPath(storageKey), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult(result);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        var path = GetPath(storageKey);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string GetPath(string storageKey)
    {
        var root = Path.GetFullPath(_root);
        var path = Path.GetFullPath(Path.Combine(root, storageKey));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid storage key.");
        return path;
    }
}
