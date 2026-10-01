namespace AIPMS.Infrastructure.Email;

public sealed class PasswordRecoverySettings
{
    public bool Enabled { get; set; }
    internal bool IsReady { get; set; }
    public string KeyRingPath { get; set; } = "";
    public string LookupKey { get; set; } = "";
    public int IntervalSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 20;
    public int LeaseSeconds { get; set; } = 120;
    public int MaxAttempts { get; set; } = 5;
    public static TimeSpan RetryDelay(int attempt) => TimeSpan.FromSeconds(30 * Math.Pow(2, Math.Clamp(attempt - 1, 0, 3)));
}
