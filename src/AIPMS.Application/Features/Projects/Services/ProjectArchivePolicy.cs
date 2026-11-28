namespace AIPMS.Application.Features.Projects.Services;

public static class ProjectArchivePolicy
{
    public static bool HasScope(bool admin, bool staff, long? departmentId, IReadOnlyCollection<long> projectDepartments) =>
        HasLeadScope(staff, departmentId, null, projectDepartments);

    public static bool HasLeadScope(bool staff, long? departmentId, long? leadDepartmentId, IReadOnlyCollection<long> projectDepartments)
    {
        if (!staff || !departmentId.HasValue) return false;
        if (leadDepartmentId.HasValue) return departmentId.Value == leadDepartmentId.Value;
        var distinct = projectDepartments.Distinct().ToArray();
        return distinct.Length == 1 && distinct[0] == departmentId.Value;
    }

    public static bool HasState(string status) => status == "COMPLETED";
}
