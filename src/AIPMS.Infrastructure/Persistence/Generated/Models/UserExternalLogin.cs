namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class UserExternalLogin
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Provider { get; set; } = "GOOGLE";
    public string Subject { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
