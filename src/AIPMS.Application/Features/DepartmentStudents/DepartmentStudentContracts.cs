using AIPMS.Application.Features.StudentRosters;

namespace AIPMS.Application.Features.DepartmentStudents;

public sealed record DepartmentSpecializationDto(
    long Id,
    long DepartmentId,
    long? MajorId,
    string? MajorCode,
    string? MajorName,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CreateDepartmentSpecializationRequest(
    long? MajorId,
    string Code,
    string Name,
    string? Description);

public sealed record UpdateDepartmentSpecializationRequest(
    long? MajorId,
    string Code,
    string Name,
    string? Description,
    bool IsActive);

public sealed record DepartmentStudentQuery(
    long? SemesterId = null,
    string? Search = null,
    long? MajorId = null,
    long? SpecializationId = null,
    string? ClassCode = null,
    string? CurriculumCode = null,
    string? ProfileStatus = null,
    string? QualificationStatus = null,
    string? TeamStatus = null,
    int Page = 1,
    int PageSize = 20);

public sealed record DepartmentStudentSummaryDto(
    long UserId,
    string? StudentCode,
    string FullName,
    string Email,
    string? Phone,
    string Status,
    long? DepartmentId,
    string? DepartmentName,
    long? MajorId,
    string? MajorCode,
    string? MajorName,
    string? ClassCode,
    long? SpecializationId,
    string? SpecializationCode,
    string? SpecializationName,
    string? CurriculumCode,
    string AcademicProfileStatus,
    string QualificationStatus,
    long? TeamId,
    string? TeamCode,
    bool IsTeamLeader,
    long? ProjectId,
    string? ProjectCode,
    string? ProjectStatus,
    DateTime? LastLoginAt);

public sealed record DepartmentStudentDirectoryStatsDto(
    long TotalStudents,
    long PendingVerificationCount,
    long QualifiedCount,
    long WithoutTeamCount);

public sealed record DepartmentStudentDirectoryPageDto(
    IReadOnlyList<DepartmentStudentSummaryDto> Items,
    int Page,
    int PageSize,
    long TotalCount,
    int TotalPages,
    DepartmentStudentDirectoryStatsDto Stats);

public sealed record DepartmentStudentDetailDto(
    DepartmentStudentSummaryDto Summary,
    string? AcademicProfileRejectionReason,
    DateTime? AcademicProfileReviewedAt,
    long? AcademicProfileReviewedBy,
    long? QualificationId,
    string? TrainingStatus,
    string? CertificateNumber,
    long? CertificateFileId,
    DateTime? QualificationIssuedAt,
    DateTime? QualificationExpiresAt,
    string? QualificationRejectionReason,
    string? QualificationConcurrencyToken,
    string? ProjectTitle,
    string? TrackCode,
    string ConcurrencyToken);

public sealed record UpdateDepartmentStudentAcademicRequest(
    long? MajorId,
    string? ClassCode,
    long? SpecializationId,
    string? CurriculumCode,
    string ConcurrencyToken);

public sealed record UpdateMyStudentAcademicRequest(
    string ClassCode,
    long? SpecializationId);

public sealed record AdminUserAccountExportQuery(
    string? Search = null,
    string? Status = null,
    string? Role = null,
    long? DepartmentId = null,
    long? MajorId = null);
