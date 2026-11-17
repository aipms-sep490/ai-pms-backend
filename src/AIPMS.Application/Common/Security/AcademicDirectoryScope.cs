namespace AIPMS.Application.Common.Security;

// Null organization means a persisted system administrator may read the global catalog.
public sealed record AcademicDirectoryScope(long? OrganizationId, long? StaffDepartmentId);

public interface IAcademicDirectoryScopeReader
{
    Task<AcademicDirectoryScope> GetAsync(CancellationToken cancellationToken = default);
}
