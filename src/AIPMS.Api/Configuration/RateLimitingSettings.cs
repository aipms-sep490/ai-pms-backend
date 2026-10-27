namespace AIPMS.Api.Configuration;

public sealed class RateLimitingSettings
{
    public const string SectionName = "RateLimiting";

    public AuthenticationRateLimitSettings Authentication { get; set; } = new();
}

public sealed class AuthenticationRateLimitSettings
{
    public int PermitLimit { get; set; } = 10;
    public int LoopbackPermitLimit { get; set; } = 300;
    public int AggregatePermitLimit { get; set; } = 10;
    public int AggregateLoopbackPermitLimit { get; set; } = 300;
    public int WindowMinutes { get; set; } = 1;
}
