using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AIPMS.AI.Configuration;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using Microsoft.Extensions.Options;

namespace AIPMS.AI.Providers;

public sealed class OpenAiTextGenerationProvider(
    HttpClient http,
    IOptions<AiAssistantOptions> settings) : IAiTextGenerationProvider
{
    private readonly AiAssistantOptions options = settings.Value;

    public string ModelVersion => $"OPENAI_{options.OpenAi.Model}";
    public string ConfigurationVersion => $"OPENAI_T{options.TimeoutSeconds}";

    public async Task<string> GenerateTextAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.OpenAi.ApiKey))
            throw new InvalidOperationException("OpenAI API key is not configured.");

        using var request = new HttpRequestMessage(HttpMethod.Post, options.OpenAi.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.OpenAi.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model = options.OpenAi.Model,
            temperature = 0.1,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        }), Encoding.UTF8, "application/json");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 120)));
        using var response = await http.SendAsync(request, timeout.Token);
        var body = await response.Content.ReadAsStringAsync(timeout.Token);
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }
}
