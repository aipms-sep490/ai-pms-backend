namespace AIPMS.AI.Configuration;

public sealed class AiAssistantOptions
{
    public const string SectionName = "AiAssistant";

    public string Provider { get; set; } = "Grounded";
    public bool Enabled { get; set; } = false;
    public bool SuiteEnabled { get; set; } = false;
    public bool MeetingAiEnabled { get; set; } = false;
    public bool ChatAiEnabled { get; set; } = false;
    public bool InsightsEnabled { get; set; } = false;
    public int DailyQuotaPerUser { get; set; } = 100;
    public bool RubricGradingCopilotEnabled { get; set; } = false;
    public bool AppealReviewAssistEnabled { get; set; } = false;
    public bool ContributionFairnessEnabled { get; set; } = false;
    public bool LegacyProjectAskEnabled { get; set; } = false;
    public bool SuggestEvaluationSchemeEnabled { get; set; } = false;
    public bool ScoreCalibrationEnabled { get; set; } = false;
    public bool KnowledgeEnabled { get; set; } = false;
    public bool DeliverableReviewEnabled { get; set; } = false;
    public bool ProgressRiskEnabled { get; set; } = false;
    public bool TopicSimilarityEnabled { get; set; } = false;
    public bool PreSemesterAiEnabled { get; set; } = false;
    public bool WorkflowAssistanceEnabled { get; set; } = false;
    public int TimeoutSeconds { get; set; } = 30;
    public bool SimulateTimeout { get; set; } = false;
    public bool SimulateFailure { get; set; } = false;
    public OpenAiOptions OpenAi { get; set; } = new();
}

public sealed class OpenAiOptions
{
    public string Endpoint { get; set; } = "https://api.openai.com/v1/chat/completions";
    public string Model { get; set; } = "gpt-4o-mini";
    public string ApiKey { get; set; } = "";
    public int MaxCompletionTokens { get; set; } = 1200;
    public string TranscriptionEndpoint { get; set; } = "";
    public string TranscriptionModel { get; set; } = "whisper-1";
}
