namespace AIPMS.Application.Features.Projects.Services;

public static class ProjectArchivePolicy
{
    public static bool HasScope(bool admin, bool staff, long? departmentId, IReadOnlyCollection<long> projectDepartments) =>
        admin || staff && departmentId.HasValue && projectDepartments.Contains(departmentId.Value);

    public static bool HasState(string status) => status == "COMPLETED";
}
