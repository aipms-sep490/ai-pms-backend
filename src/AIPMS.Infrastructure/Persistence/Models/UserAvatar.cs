using System;

namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class UserAvatar
{
    public long UserId { get; set; }
    public string StorageKey { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public Guid ConcurrencyToken { get; set; }
    public DateTime UpdatedAt { get; set; }
}
