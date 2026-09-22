namespace AIPMS.Application.Features.StudentQualifications.Models;

public static class StudentQualificationTypes
{
    public const string CapstoneReadiness = "CAPSTONE_READINESS";
}

public static class StudentTrainingStatuses
{
    public const string PendingTraining = "PENDING_TRAINING";
    public const string Completed = "TRAINING_COMPLETED";

    public static bool IsValid(string value) =>
        value is PendingTraining or Completed;
}

public static class StudentQualificationStatuses
{
    public const string PendingVerification = "PENDING_VERIFICATION";
    public const string Verified = "VERIFIED";
    public const string Rejected = "REJECTED";
    public const string Expired = "EXPIRED";

    public static bool IsValid(string value) =>
        value is PendingVerification or Verified or Rejected or Expired;
}

public sealed record StudentQualificationModel(
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
    DateTime UpdatedAt);

public sealed record ProjectPeriodQualificationPolicyModel(
    long ProjectPeriodId,
    bool RequireStudentQualification,
    string QualificationType,
    bool RequireCertificate,
    bool CheckExpiration,
    DateTime UpdatedAt);
