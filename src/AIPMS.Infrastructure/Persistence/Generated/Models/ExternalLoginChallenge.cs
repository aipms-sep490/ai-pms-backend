namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class ExternalLoginChallenge
{
    public Guid Id { get; set; }
    public string Purpose { get; set; } = "";
    public long? UserId { get; set; }
    public byte[] NonceHash { get; set; } = [];
    public byte[] BrowserHash { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
}
