using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class PasswordResetToken
{
    public long Id { get; set; }

    public long UserId { get; set; }

    public byte[] TokenHash { get; set; } = null!;

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public string? RequestedByIp { get; set; }

    public DateTime? UsedAt { get; set; }

    public virtual User User { get; set; } = null!;
}
