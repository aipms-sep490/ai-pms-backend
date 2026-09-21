using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Academic.Abstractions;
using AIPMS.Application.Features.StudentQualifications.Abstractions;
using AIPMS.Application.Features.StudentQualifications.DTOs;
using AIPMS.Application.Features.StudentQualifications.Models;

namespace AIPMS.Application.Features.StudentQualifications.Services;

public sealed class StudentQualificationWorkflow(
    IStudentQualificationRepository repository,
    IAcademicStructureRepository academic,
    ICurrentUser currentUser,
    IAuditTrail audit,
    TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<StudentQualificationDto?> MineAsync(
        string qualificationType,
        CancellationToken cancellationToken)
    {
        var actorId = RequireRole(AppRoles.Student);
        var result = await repository.GetForUserAsync(
            actorId, NormalizeType(qualificationType), cancellationToken);
        return result?.ToDto();
    }

    public async Task<StudentQualificationDto> SubmitEvidenceAsync(
        SubmitStudentQualificationEvidenceRequest request,
        CancellationToken cancellationToken)
    {
        var actorId = RequireRole(AppRoles.Student);
        var qualificationType = NormalizeType(request.QualificationType);
        var trainingStatus = request.TrainingStatus.Trim().ToUpperInvariant();

        if (!StudentTrainingStatuses.IsValid(trainingStatus))
            throw new ConflictException("Training status is not supported.");
        if (trainingStatus != StudentTrainingStatuses.Completed)
            throw new ConflictException("Evidence can only be submitted after the required training is completed.");
        if (request.ExpiresAt.HasValue && request.IssuedAt.HasValue
            && request.ExpiresAt.Value <= request.IssuedAt.Value)
            throw new ConflictException("Certificate expiration must be later than its issue time.");

        var result = await repository.SubmitEvidenceAsync(
            actorId, qualificationType, trainingStatus,
            Trim(request.CertificateNumber), request.CertificateFileId,
            request.IssuedAt, request.ExpiresAt, Now, cancellationToken);

        await audit.RecordAsync(new AuditEntry(
            actorId, "STUDENT_QUALIFICATION_EVIDENCE_SUBMITTED",
            "STUDENT_QUALIFICATION", result.Id,
            new Dictionary<string, object?>
            {
                ["qualificationType"] = result.QualificationType,
                ["trainingStatus"] = result.TrainingStatus,
                ["verificationStatus"] = result.VerificationStatus
            }), cancellationToken);

        return result.ToDto();
    }

    public async Task<PagedResult<StudentQualificationDto>> QueueAsync(
        string? verificationStatus,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var actorId = RequireRole(AppRoles.DepartmentStaff);
        var scope = await academic.GetUserScopeAsync(actorId, cancellationToken)
            ?? throw new ForbiddenException("The department staff account has no active academic scope.");

        var status = string.IsNullOrWhiteSpace(verificationStatus)
            ? StudentQualificationStatuses.PendingVerification
            : verificationStatus.Trim().ToUpperInvariant();

        if (!StudentQualificationStatuses.IsValid(status))
            throw new ConflictException("Qualification verification status is not supported.");

        var result = await repository.SearchVerificationQueueAsync(
            scope.OrganizationId, scope.DepartmentId, status, Trim(search),
            page, pageSize, cancellationToken);

        return new(
            result.Items.Select(x => x.ToDto()).ToArray(),
            result.Page, result.PageSize, result.TotalCount);
    }

    public Task<StudentQualificationDto> VerifyAsync(
        long qualificationId,
        CancellationToken cancellationToken) =>
        DecideAsync(qualificationId, StudentQualificationStatuses.Verified, null, cancellationToken);

    public Task<StudentQualificationDto> RejectAsync(
        long qualificationId,
        string? reason,
        CancellationToken cancellationToken) =>
        DecideAsync(qualificationId, StudentQualificationStatuses.Rejected, reason, cancellationToken);

    private async Task<StudentQualificationDto> DecideAsync(
        long qualificationId,
        string status,
        string? reason,
        CancellationToken cancellationToken)
    {
        var actorId = RequireRole(AppRoles.DepartmentStaff);
        var scope = await academic.GetUserScopeAsync(actorId, cancellationToken)
            ?? throw new ForbiddenException("The department staff account has no active academic scope.");
        var current = await repository.GetAsync(qualificationId, cancellationToken)
            ?? throw new NotFoundException("StudentQualification", qualificationId);

        if (current.OrganizationId != scope.OrganizationId || current.DepartmentId != scope.DepartmentId)
            throw new ForbiddenException("Department staff can only verify students in their assigned department.");
        if (current.VerificationStatus != StudentQualificationStatuses.PendingVerification)
            throw new ConflictException("Only a pending qualification can be verified or rejected.");
        if (status == StudentQualificationStatuses.Verified
            && current.TrainingStatus != StudentTrainingStatuses.Completed)
            throw new ConflictException("Training must be completed before qualification verification.");
        if (status == StudentQualificationStatuses.Rejected && string.IsNullOrWhiteSpace(reason))
            throw new ConflictException("A rejection reason is required.");

        var result = await repository.DecideAsync(
            qualificationId, status, actorId, Trim(reason), Now, cancellationToken);

        await audit.RecordAsync(new AuditEntry(
            actorId,
            status == StudentQualificationStatuses.Verified
                ? "STUDENT_QUALIFICATION_VERIFIED"
                : "STUDENT_QUALIFICATION_REJECTED",
            "STUDENT_QUALIFICATION", result.Id,
            new Dictionary<string, object?>
            {
                ["studentUserId"] = result.UserId,
                ["qualificationType"] = result.QualificationType,
                ["verificationStatus"] = result.VerificationStatus,
                ["reason"] = result.RejectionReason
            }), cancellationToken);

        return result.ToDto();
    }

    private long RequireRole(string role)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not long actorId)
            throw new UnauthorizedException();
        if (!currentUser.Roles.Contains(role))
            throw new ForbiddenException();
        return actorId;
    }

    private static string NormalizeType(string value)
    {
        var normalized = value.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 50)
            throw new ConflictException("Qualification type is required and must not exceed 50 characters.");
        return normalized;
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
