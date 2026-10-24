using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AIPMS.AI.Configuration;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using Microsoft.Extensions.Options;

namespace AIPMS.AI.Providers;

public sealed class OpenAiTextGenerationProvider(
    HttpClient http,
    IOptions<AiAssistantOptions> settings) : IAiTextGenerationProvider, IAiVisionProvider
{
    private readonly AiAssistantOptions options = settings.Value;

    public string ModelVersion => $"OPENAI_{options.OpenAi.Model}";
    public string ConfigurationVersion => $"OPENAI_T{options.TimeoutSeconds}";

    public Task<string> GenerateTextAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default) =>
        GenerateAsync(systemPrompt, userPrompt, cancellationToken);

    public Task<string> GenerateWithImagesAsync(string systemPrompt, string userPrompt, IReadOnlyList<AiImageInput> images, CancellationToken cancellationToken = default)
    {
        var content = new List<object> { new { type = "text", text = userPrompt } };
        content.AddRange(images.Select(image => (object)new
        {
            type = "image_url",
            image_url = new { url = $"data:{image.MediaType};base64,{Convert.ToBase64String(image.Content)}" }
        }));
        return GenerateAsync(systemPrompt, content, cancellationToken);
    }

    private async Task<string> GenerateAsync(string systemPrompt, object userContent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.OpenAi.ApiKey))
            throw new InvalidOperationException("OpenAI API key is not configured.");

        using var request = new HttpRequestMessage(HttpMethod.Post, options.OpenAi.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.OpenAi.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model = options.OpenAi.Model,
            temperature = 0.1,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userContent }
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
