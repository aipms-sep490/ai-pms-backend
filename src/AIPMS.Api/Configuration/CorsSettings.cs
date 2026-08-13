using System.ComponentModel.DataAnnotations;

namespace AIPMS.Api.Configuration;

public sealed class CorsSettings
{
    public const string SectionName = "Cors";

    public const string FrontendPolicyName = "Frontend";

    [Required]
    [MinLength(1)]
    public string[] AllowedOrigins { get; init; } = [];
}
