namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class StudentQualification
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long OrganizationId { get; set; }
    public string QualificationType { get; set; } = null!;
    public string TrainingStatus { get; set; } = null!;
    public string VerificationStatus { get; set; } = null!;
    public string? CertificateNumber { get; set; }
    public long? CertificateFileId { get; set; }
    public DateTime? IssuedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public long? VerifiedBy { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? RejectionReason { get; set; }
    public Guid ConcurrencyToken { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class ProjectPeriodQualificationPolicy
{
    public long ProjectPeriodId { get; set; }
    public bool RequireStudentQualification { get; set; }
    public string QualificationType { get; set; } = "CAPSTONE_READINESS";
    public bool RequireCertificate { get; set; } = true;
    public bool CheckExpiration { get; set; } = true;
    public DateTime UpdatedAt { get; set; }
}
