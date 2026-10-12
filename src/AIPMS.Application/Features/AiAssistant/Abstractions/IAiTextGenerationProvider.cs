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
