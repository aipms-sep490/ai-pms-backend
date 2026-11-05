namespace AIPMS.Application.Features.Evaluations.Models;

public sealed class RubricOptions
{
    public const string SectionName = "Rubric";
    public int RequiredLevelCount { get; set; } = 4;
}
