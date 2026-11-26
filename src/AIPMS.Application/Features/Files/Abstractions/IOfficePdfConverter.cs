using System.Threading;
using System.Threading.Tasks;

namespace AIPMS.Application.Features.Files.Abstractions;

public interface IOfficePdfConverter
{
    bool IsEnabled { get; }
    Task<byte[]> ConvertToPdfAsync(string fileName, byte[] bytes, CancellationToken ct = default);
}
