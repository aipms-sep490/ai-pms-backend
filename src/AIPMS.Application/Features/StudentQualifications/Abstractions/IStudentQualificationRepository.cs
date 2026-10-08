using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.StudentQualifications.Models;

namespace AIPMS.Application.Features.StudentQualifications.Abstractions;

public interface IStudentQualificationRepository
{
    Task<long> AddCertificateFileAsync(long actorId, string storageKey,
        AIPMS.Application.Features.Deliverables.Models.ValidatedUpload file, DateTime now, CancellationToken ct);
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken);

    Task<StudentQualificationModel?> GetForUserAsync(
        long userId,
        string qualificationType,
        CancellationToken cancellationToken = default);

    Task<StudentQualificationModel?> GetAsync(
        long qualificationId,
        CancellationToken cancellationToken = default);

    Task<StudentQualificationCertificateModel?> GetCertificateAsync(
        long qualificationId,
        CancellationToken cancellationToken = default);

    Task<PagedResult<StudentQualificationModel>> SearchVerificationQueueAsync(
        long organizationId,
        long departmentId,
        string? verificationStatus,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<StudentQualificationModel> SubmitEvidenceAsync(
        long userId,
        string qualificationType,
        string trainingStatus,
        string? certificateNumber,
        long? certificateFileId,
        DateTime? issuedAt,
        DateTime? expiresAt,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<StudentQualificationModel> DecideAsync(
        long qualificationId,
        string verificationStatus,
        long actorUserId,
        string? reason,
        Guid? expectedConcurrencyToken,
        DateTime utcNow,
        CancellationToken cancellationToken = default);
    Task<ProjectPeriodQualificationPolicyModel?> GetPeriodPolicyAsync(
        long projectPeriodId,
        CancellationToken cancellationToken = default);

    Task<ProjectPeriodQualificationPolicyModel> SetPeriodPolicyAsync(
        long projectPeriodId,
        bool requireStudentQualification,
        string qualificationType,
        bool requireCertificate,
        bool checkExpiration,
        DateTime utcNow,
        CancellationToken cancellationToken = default);
}

