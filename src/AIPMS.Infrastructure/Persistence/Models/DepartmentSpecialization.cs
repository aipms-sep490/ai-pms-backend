using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class DepartmentSpecialization
{
    public long Id { get; set; }
    public long DepartmentId { get; set; }
    public long? MajorId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public M.Department Department { get; set; } = null!;
    public M.Major? Major { get; set; }
}
