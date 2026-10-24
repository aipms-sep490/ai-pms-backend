using System.Threading;
using System.Threading.Tasks;

namespace AIPMS.Application.Features.AiAssistant.Abstractions;

public interface IAiTextGenerationProvider
{
    string ModelVersion => "UNKNOWN";
    string ConfigurationVersion => "UNKNOWN";
    Task<string> GenerateTextAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);
}

public sealed record AiImageInput(string MediaType, byte[] Content);

public interface IAiVisionProvider
{
    Task<string> GenerateWithImagesAsync(string systemPrompt, string userPrompt,
        IReadOnlyList<AiImageInput> images, CancellationToken cancellationToken = default);
}
