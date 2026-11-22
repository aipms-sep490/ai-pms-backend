using System;

namespace AIPMS.Infrastructure.Services;

internal static class AiJsonSanitizer
{
    /// <summary>
    /// Strips optional Markdown code fences (```json ... ```) around a JSON object
    /// emitted by OpenAI-compatible providers without accepting arbitrary prose.
    /// </summary>
    internal static string ExtractJsonObject(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var text = raw.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = text.IndexOf('\n');
            if (firstNewLine < 0) return text;
            var body = text[(firstNewLine + 1)..];
            var closingFence = body.LastIndexOf("```", StringComparison.Ordinal);
            if (closingFence >= 0)
            {
                text = body[..closingFence].Trim();
            }
        }
        return text;
    }
}
