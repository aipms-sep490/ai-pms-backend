using System.IO;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Storage;
using Microsoft.Extensions.Configuration;

namespace AIPMS.Infrastructure.Storage;

internal sealed class LocalFileStorage : IFileStorage
{
    private readonly string root;
    public LocalFileStorage(IConfiguration configuration)
    {
        var configured = configuration["FileStorage:RootPath"];
        root = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIPMS", "private-files")
            : configured);
    }

    private string Resolve(string key)
    {
        if (key.Length != 32 || key.Any(c => !char.IsAsciiHexDigit(c)))
            throw new ArgumentException("Invalid storage key.");
        return Path.Combine(root, key);
    }

    public async Task WriteAsync(string key, Stream content, CancellationToken ct)
    {
        var target = Resolve(key);
        Directory.CreateDirectory(root);
        // CreateNew prevents overwriting an existing object, including on accidental key reuse.
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            81920, FileOptions.Asynchronous);
        try { await content.CopyToAsync(output, ct); await output.FlushAsync(ct); }
        catch
        {
            await output.DisposeAsync();
            System.IO.File.Delete(target);
            throw;
        }
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<Stream>(new FileStream(Resolve(key), FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan));
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        System.IO.File.Delete(Resolve(key));
        return Task.CompletedTask;
    }
}
