using System.Text.Json;

namespace AIPMS.IntegrationTests;

// Opt-in, allowlisted evidence from synthetic isolated fixtures. Never record headers or free text.
public sealed class ApiAcceptanceEvidence(string scenario)
{
    private readonly List<object> exchanges = [];
    private static readonly HashSet<string> Fields = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "status", "scope", "stage", "totalScore", "canPublish", "blockers", "generationStatus",
        "insufficientEvidence", "errorCode", "runId", "projectCount", "openTasks", "overdueTasks", "complete"
    };
    private static object? Projection(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            return document.RootElement.EnumerateObject().Where(p => Fields.Contains(p.Name))
                .ToDictionary(p => p.Name, p => p.Value.Clone());
        }
        catch (JsonException) { return null; }
    }
    public DelegatingHandler Handler() => new Recorder(this);
    public async Task SaveAsync(object readback)
    {
        var directory = Environment.GetEnvironmentVariable("AIPMS_ACCEPTANCE_OUTPUT");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        await System.IO.File.WriteAllTextAsync(Path.Combine(directory, scenario + ".json"), JsonSerializer.Serialize(new
        {
            scenario, synthetic = true, isolatedDatabase = true, authentication = "test JWT; persisted resource authorization exercised",
            storage = "in-memory storage adapter where files are used", passed = true,
            capturedAtUtc = DateTime.UtcNow, exchanges, readback
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private sealed class Recorder(ApiAcceptanceEvidence evidence) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var correlationId = Guid.NewGuid().ToString();
            request.Headers.Add("X-Correlation-ID", correlationId);
            var jsonRequest = request.Content?.Headers.ContentType?.MediaType == "application/json"
                ? await request.Content.ReadAsStringAsync(ct) : null;
            var response = await base.SendAsync(request, ct);
            var jsonResponse = response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.Ordinal) == true
                ? await response.Content.ReadAsStringAsync(ct) : null;
            lock (evidence.exchanges) evidence.exchanges.Add(new { method = request.Method.Method,
                path = request.RequestUri!.AbsolutePath, correlationId, statusCode = (int)response.StatusCode,
                request = Projection(jsonRequest), response = Projection(jsonResponse) });
            return response;
        }
    }
}
