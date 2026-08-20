namespace AIPMS.Application.Common.Security;

public static class AuthorizationPolicies
{
    public const string AdminOnly = "AdminOnly";
    public const string AcademicManagement = "AcademicManagement";
    public const string LecturerOnly = "LecturerOnly";
    public const string StudentOnly = "StudentOnly";
    public const string ProjectAccess = "ProjectAccess";
}
