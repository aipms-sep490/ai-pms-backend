using AIPMS.Application.Features.StudentQualifications.Models;

namespace AIPMS.Application.Features.StudentQualifications.DTOs;

public sealed record StudentQualificationDto(
    long Id,
    long UserId,
    string FullName,
    string? StudentCode,
    long OrganizationId,
    long DepartmentId,
    long? MajorId,
    string QualificationType,
    string TrainingStatus,
    string VerificationStatus,
    string? CertificateNumber,
    long? CertificateFileId,
    DateTime? IssuedAt,
    DateTime? ExpiresAt,
    long? VerifiedBy,
    DateTime? VerifiedAt,
    string? RejectionReason,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public bool IsCurrentlyVerified(DateTime utcNow) =>
        VerificationStatus == StudentQualificationStatuses.Verified
        && (!ExpiresAt.HasValue || ExpiresAt.Value > utcNow);
}

public sealed record SubmitStudentQualificationEvidenceRequest(
    string QualificationType,
    string TrainingStatus,
    string? CertificateNumber,
    long? CertificateFileId,
    DateTime? IssuedAt,
    DateTime? ExpiresAt);

public sealed record DecideStudentQualificationRequest(string? Reason);

public static class StudentQualificationDtoMapper
{
    public static StudentQualificationDto ToDto(this StudentQualificationModel model) => new(
        model.Id, model.UserId, model.FullName, model.StudentCode, model.OrganizationId,
        model.DepartmentId, model.MajorId, model.QualificationType, model.TrainingStatus,
        model.VerificationStatus, model.CertificateNumber, model.CertificateFileId,
        model.IssuedAt, model.ExpiresAt, model.VerifiedBy, model.VerifiedAt,
        model.RejectionReason, model.CreatedAt, model.UpdatedAt);
}
