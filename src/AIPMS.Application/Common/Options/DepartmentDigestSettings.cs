namespace AIPMS.Application.Common.Options;

public sealed class DepartmentDigestSettings
{
    public const string SectionName = "DepartmentDigest";

    public bool Enabled { get; set; }
    public int HourUtc { get; set; } = 1;
    public int BatchSize { get; set; } = 50;
}
