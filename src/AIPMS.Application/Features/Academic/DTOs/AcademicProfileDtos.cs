namespace AIPMS.Application.Features.Academic.DTOs;

public sealed record AcademicProfileDto(
    long UserId,
    string FullName,
    string? Email,
    string? StudentCode,
    long? DepartmentId,
    string? DepartmentName,
    long? MajorId,
    string? MajorName,
    string Status,
    long? ReviewedBy,
    DateTime? ReviewedAt,
    string? RejectionReason,
    string? ConcurrencyToken = null,
    string? ClassCode = null,
    long? SpecializationId = null,
    string? SpecializationCode = null,
    string? SpecializationName = null,
    string? CurriculumCode = null);

public sealed record UpdateAcademicProfileRequest(
    long? DepartmentId,
    long? MajorId,
    string ConcurrencyToken);

public sealed record RejectAcademicProfileRequest(string Reason);
