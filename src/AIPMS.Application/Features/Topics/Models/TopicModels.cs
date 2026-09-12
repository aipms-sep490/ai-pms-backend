namespace AIPMS.Application.Features.Topics.Models;

public sealed record TopicActor(long Id, long? DepartmentId, long? OrganizationId, long? MajorId,
    bool IsAdmin, bool IsStaff, bool IsLecturer, bool IsStudent, bool HasActiveScope, bool HasEligibleStudentProfile);
public sealed record TopicPeriod(long Id, long SemesterId, long OrganizationId, string PeriodType,
    string Status, string SemesterStatus, DateTime StartAt, DateTime EndAt, DateOnly SemesterEnd, bool ActiveOrganization);
public sealed record TopicMajor(long Id, long DepartmentId, long OrganizationId, bool IsActive);
public sealed record TopicFilter(long? AcademicSemesterId, long? ProjectPeriodId, long? DepartmentId, long? MajorId,
    string? ProjectMode, string Status = "PUBLISHED", string? Search = null, bool CompatibleOnly = false,
    bool MineOnly = false, int Page = 1, int PageSize = 20);
