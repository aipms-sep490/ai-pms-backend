namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class AiSetupApplication
{
    public long Id { get; set; }
    public long ActorId { get; set; }
    public Guid IdempotencyKey { get; set; }
    public string ScopeType { get; set; } = "PROJECT";
    public long? ProjectId { get; set; }
    public long? RunId { get; set; }
    public string ResultJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
}
