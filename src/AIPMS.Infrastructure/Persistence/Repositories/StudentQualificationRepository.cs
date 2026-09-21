using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.StudentQualifications.Abstractions;
using AIPMS.Application.Features.StudentQualifications.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class StudentQualificationRepository(AipmsDbContext context)
    : IStudentQualificationRepository
{
    private IQueryable<StudentQualificationModel> Query()
    {
        var q =
            from qualification in context.Set<StudentQualification>().AsNoTracking()
            join user in context.Users.AsNoTracking() on qualification.UserId equals user.Id
            select new StudentQualificationModel(
                qualification.Id,
                qualification.UserId,
                user.FullName,
                user.StudentCode,
                qualification.OrganizationId,
                user.Major != null ? user.Major.DepartmentId : (user.DepartmentId ?? 0),
                user.MajorId,
                qualification.QualificationType,
                qualification.TrainingStatus,
                qualification.VerificationStatus,
                qualification.CertificateNumber,
                qualification.CertificateFileId,
                qualification.IssuedAt,
                qualification.ExpiresAt,
                qualification.VerifiedBy,
                qualification.VerifiedAt,
                qualification.RejectionReason,
                qualification.CreatedAt,
                qualification.UpdatedAt);

        return q;
    }

    public Task<StudentQualificationModel?> GetForUserAsync(
        long userId,
        string qualificationType,
        CancellationToken cancellationToken = default) =>
        Query().SingleOrDefaultAsync(
            x => x.UserId == userId && x.QualificationType == qualificationType,
            cancellationToken);

    public Task<StudentQualificationModel?> GetAsync(
        long qualificationId,
        CancellationToken cancellationToken = default) =>
        Query().SingleOrDefaultAsync(x => x.Id == qualificationId, cancellationToken);

    public async Task<PagedResult<StudentQualificationModel>> SearchVerificationQueueAsync(
        long organizationId,
        long departmentId,
        string? verificationStatus,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = Query().Where(x =>
            x.OrganizationId == organizationId && x.DepartmentId == departmentId);

        if (!string.IsNullOrWhiteSpace(verificationStatus))
            query = query.Where(x => x.VerificationStatus == verificationStatus);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim();
            query = query.Where(x =>
                x.FullName.Contains(value)
                || (x.StudentCode != null && x.StudentCode.Contains(value)));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.VerificationStatus)
            .ThenByDescending(x => x.UpdatedAt)
            .ThenBy(x => x.UserId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new(items, page, pageSize, total);
    }

    public async Task<StudentQualificationModel> SubmitEvidenceAsync(
        long userId,
        string qualificationType,
        string trainingStatus,
        string? certificateNumber,
        long? certificateFileId,
        DateTime? issuedAt,
        DateTime? expiresAt,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var student = await context.Users
            .Where(u => u.Id == userId
                && u.Status == "ACTIVE"
                && u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.Student)
                && u.Major != null
                && u.Major.IsActive
                && u.Major.Department.IsActive
                && u.Major.Department.Organization.IsActive)
            .Select(u => new
            {
                OrganizationId = u.Major!.Department.OrganizationId,
                DepartmentId = u.Major.DepartmentId
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ConflictException("An active academic student profile is required before submitting qualification evidence.");

        if (certificateFileId.HasValue)
        {
            var fileExists = await context.Files.AsNoTracking()
                .AnyAsync(f => f.Id == certificateFileId.Value && f.UploadedBy == userId, cancellationToken);
            if (!fileExists)
                throw new ConflictException("The qualification evidence file must belong to the current student.");
        }

        var entity = await context.Set<StudentQualification>()
            .SingleOrDefaultAsync(
                x => x.UserId == userId && x.QualificationType == qualificationType,
                cancellationToken);

        if (entity is null)
        {
            entity = new StudentQualification
            {
                UserId = userId,
                OrganizationId = student.OrganizationId,
                QualificationType = qualificationType,
                CreatedAt = utcNow
            };
            context.Add(entity);
        }

        entity.OrganizationId = student.OrganizationId;
        entity.TrainingStatus = trainingStatus;
        entity.VerificationStatus = StudentQualificationStatuses.PendingVerification;
        entity.CertificateNumber = certificateNumber;
        entity.CertificateFileId = certificateFileId;
        entity.IssuedAt = issuedAt;
        entity.ExpiresAt = expiresAt;
        entity.VerifiedBy = null;
        entity.VerifiedAt = null;
        entity.RejectionReason = null;
        entity.ConcurrencyToken = Guid.NewGuid();
        entity.UpdatedAt = utcNow;

        await context.SaveChangesAsync(cancellationToken);
        return (await GetAsync(entity.Id, cancellationToken))!;
    }

    public async Task<StudentQualificationModel> DecideAsync(
        long qualificationId,
        string verificationStatus,
        long actorUserId,
        string? reason,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.Set<StudentQualification>()
            .SingleOrDefaultAsync(x => x.Id == qualificationId, cancellationToken)
            ?? throw new NotFoundException("StudentQualification", qualificationId);

        if (entity.VerificationStatus != StudentQualificationStatuses.PendingVerification)
            throw new ConflictException("The qualification was already processed.");

        entity.VerificationStatus = verificationStatus;
        entity.VerifiedBy = actorUserId;
        entity.VerifiedAt = utcNow;
        entity.RejectionReason = reason;
        entity.ConcurrencyToken = Guid.NewGuid();
        entity.UpdatedAt = utcNow;

        await context.SaveChangesAsync(cancellationToken);
        return (await GetAsync(entity.Id, cancellationToken))!;
    }

    public async Task<ProjectPeriodQualificationPolicyModel?> GetPeriodPolicyAsync(
        long projectPeriodId,
        CancellationToken cancellationToken = default) =>
        await context.Set<ProjectPeriodQualificationPolicy>()
            .AsNoTracking()
            .Where(x => x.ProjectPeriodId == projectPeriodId)
            .Select(x => new ProjectPeriodQualificationPolicyModel(
                x.ProjectPeriodId,
                x.RequireStudentQualification,
                x.QualificationType,
                x.RequireCertificate,
                x.CheckExpiration,
                x.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<ProjectPeriodQualificationPolicyModel> SetPeriodPolicyAsync(
        long projectPeriodId,
        bool requireStudentQualification,
        string qualificationType,
        bool requireCertificate,
        bool checkExpiration,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var periodExists = await context.ProjectPeriods.AsNoTracking()
            .AnyAsync(x => x.Id == projectPeriodId && x.PeriodType == "REGISTRATION", cancellationToken);
        if (!periodExists)
            throw new NotFoundException("RegistrationProjectPeriod", projectPeriodId);

        var entity = await context.Set<ProjectPeriodQualificationPolicy>()
            .SingleOrDefaultAsync(x => x.ProjectPeriodId == projectPeriodId, cancellationToken);

        if (entity is null)
        {
            entity = new ProjectPeriodQualificationPolicy { ProjectPeriodId = projectPeriodId };
            context.Add(entity);
        }

        entity.RequireStudentQualification = requireStudentQualification;
        entity.QualificationType = qualificationType;
        entity.RequireCertificate = requireCertificate;
        entity.CheckExpiration = checkExpiration;
        entity.UpdatedAt = utcNow;

        await context.SaveChangesAsync(cancellationToken);

        return (await GetPeriodPolicyAsync(projectPeriodId, cancellationToken))!;
    }
}
