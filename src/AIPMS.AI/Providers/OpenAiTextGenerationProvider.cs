using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AIPMS.AI.Configuration;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using Microsoft.Extensions.Options;

namespace AIPMS.AI.Providers;

public sealed class OpenAiTextGenerationProvider(
    HttpClient http,
    IOptions<AiAssistantOptions> settings) : IAiTextGenerationProvider, IAiVisionProvider, IAiStreamingProvider
{
    private readonly AiAssistantOptions options = settings.Value;

    public string ModelVersion => $"OPENAI_{options.OpenAi.Model}";
    public string ConfigurationVersion => $"OPENAI_T{options.TimeoutSeconds}";

    public async Task<string> GenerateStreamingAsync(string systemPrompt, string userPrompt, IReadOnlyList<AiImageInput> images,
        Func<string, Task> onDelta, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.OpenAi.ApiKey)) throw new InvalidOperationException("OpenAI API key is not configured.");
        var content = new List<object> { new { type = "text", text = userPrompt } };
        content.AddRange(images.Select(image => (object)new { type = "image_url", image_url = new { url = $"data:{image.MediaType};base64,{Convert.ToBase64String(image.Content)}" } }));
        using var request = new HttpRequestMessage(HttpMethod.Post, options.OpenAi.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.OpenAi.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(new {
            model = options.OpenAi.Model, temperature = 0.1, stream = true,
            messages = new object[] { new { role = "system", content = systemPrompt }, new { role = "user", content = (object)content } }
        }), Encoding.UTF8, "application/json");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 120)));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(timeout.Token));
        var output = new StringBuilder();
        var finished = false;
        while (await reader.ReadLineAsync(timeout.Token) is { } line)
        {
            if (line.Length > 64000) throw new JsonException("Provider frame too large.");
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") return output.ToString();
            using var frame = JsonDocument.Parse(data);
            if (!frame.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) continue;
            if (choices[0].TryGetProperty("finish_reason", out var reason) && reason.GetString() == "stop") finished = true;
            if (!choices[0].TryGetProperty("delta", out var delta) || !delta.TryGetProperty("content", out var text) || text.ValueKind != JsonValueKind.String) continue;
            var value = text.GetString() ?? "";
            output.Append(value);
            if (output.Length > 12000) throw new JsonException("Provider output too large.");
            await onDelta(value);
        }
        // OpenAI-compatible routers may close after finish_reason=stop without a [DONE] sentinel.
        if (finished) return output.ToString();
        throw new IOException("Provider stream ended before completion.");
    }

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
