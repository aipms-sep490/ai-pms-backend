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
    string? RejectionReason);

public sealed record RejectAcademicProfileRequest(string Reason);
