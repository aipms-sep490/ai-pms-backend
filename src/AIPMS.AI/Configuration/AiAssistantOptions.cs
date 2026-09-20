namespace AIPMS.AI.Configuration;

public sealed class AiAssistantOptions
{
    public const string SectionName = "AiAssistant";

    public string Provider { get; set; } = "Grounded";
    public int TimeoutSeconds { get; set; } = 10;
    public bool SimulateTimeout { get; set; } = false;
    public bool SimulateFailure { get; set; } = false;
}
