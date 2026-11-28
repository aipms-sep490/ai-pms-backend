using System.ComponentModel.DataAnnotations;

namespace AIPMS.Api.Configuration;

public sealed class DualDeptSlaSettings
{
    public const string SectionName = "DualDeptSla";

    public bool Enabled { get; set; } = true;

    [Range(1, 1440)]
    public int IntervalMinutes { get; set; } = 15;
}
