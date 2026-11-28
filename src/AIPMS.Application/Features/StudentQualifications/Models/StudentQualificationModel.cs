namespace AIPMS.Application.Features.StudentQualifications.Models;

public sealed record StudentQualificationTypeDefinition(string Code, string Name, string Description);

public static class StudentQualificationTypes
{
    public const string CapstoneReadiness = "CAPSTONE_READINESS";
    public const string CreditsThreshold = "CREDITS_THRESHOLD";
    public const string EnglishProficiency = "ENGLISH_PROFICIENCY";
    public const string InternshipCompleted = "INTERNSHIP_COMPLETED";

    public static readonly IReadOnlyList<StudentQualificationTypeDefinition> Allowed =
    [
        new(CapstoneReadiness, "Đủ điều kiện làm đồ án tốt nghiệp", "Xác nhận sinh viên hoàn tất điều kiện học vụ chung để đăng ký đồ án tốt nghiệp."),
        new(CreditsThreshold, "Đủ tín chỉ tích lũy", "Xác nhận sinh viên đã tích lũy đủ số tín chỉ tối thiểu theo khung chương trình."),
        new(EnglishProficiency, "Chuẩn ngoại ngữ", "Xác nhận sinh viên đạt chuẩn đầu ra/đầu vào ngoại ngữ theo quy định."),
        new(InternshipCompleted, "Hoàn thành thực tập doanh nghiệp", "Xác nhận sinh viên đã hoàn thành học phần thực tập doanh nghiệp (OJT).")
    ];

    public static bool IsValid(string? code) =>
        !string.IsNullOrWhiteSpace(code)
        && Allowed.Any(x => string.Equals(x.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
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
    Guid ConcurrencyToken,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? VerifiedByName = null);

public sealed record StudentQualificationCertificateModel(
    long QualificationId,
    long OwnerUserId,
    long OrganizationId,
    long DepartmentId,
    long FileId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string? ChecksumSha256,
    string StorageKey,
    long UploadedBy);

public sealed record ProjectPeriodQualificationPolicyModel(
    long ProjectPeriodId,
    bool RequireStudentQualification,
    string QualificationType,
    bool RequireCertificate,
    bool CheckExpiration,
    DateTime UpdatedAt);
