namespace AIPMS.AI.Configuration;

public sealed class AiAssistantOptions
{
    public const string SectionName = "AiAssistant";

    public string Provider { get; set; } = "Grounded";
    public bool KnowledgeEnabled { get; set; }
    public int TimeoutSeconds { get; set; } = 10;
    public bool SimulateTimeout { get; set; } = false;
    public bool SimulateFailure { get; set; } = false;
    public OpenAiOptions OpenAi { get; set; } = new();
}

public sealed class OpenAiOptions
{
    public string Endpoint { get; set; } = "https://api.openai.com/v1/chat/completions";
    public string Model { get; set; } = "gpt-4o-mini";
    public string ApiKey { get; set; } = "";
}
