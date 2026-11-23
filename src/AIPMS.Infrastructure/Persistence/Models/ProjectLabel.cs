using System;

namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class ProjectLabel
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string ColorHex { get; set; } = "#64748B";
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}
