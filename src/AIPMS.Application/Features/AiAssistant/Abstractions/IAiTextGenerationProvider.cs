using System.Threading;
using System.Threading.Tasks;

namespace AIPMS.Application.Features.AiAssistant.Abstractions;

public interface IAiTextGenerationProvider
{
    Task<string> GenerateTextAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);
}
