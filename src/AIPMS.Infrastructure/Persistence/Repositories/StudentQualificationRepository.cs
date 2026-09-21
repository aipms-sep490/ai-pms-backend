using System.Data;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.StudentQualifications.Abstractions;
using AIPMS.Application.Features.StudentQualifications.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class StudentQualificationRepository(AipmsDbContext context)
    : IStudentQualificationRepository
{
    public async Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is not null)
            return await action(cancellationToken);

        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var result = await action(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            for (var cause = exception; cause is not null; cause = cause.InnerException)
                if (cause is DbUpdateConcurrencyException or SqlException { Number: 1205 or 2601 or 2627 })
                    throw new ConflictException("The qualification changed concurrently. Reload and retry.");
            throw;
        }
    }

    private IQueryable<StudentQualificationModel> Query(
        long? qualificationId = null,
        long? userId = null,
        string? qualificationType = null,
        long? organizationId = null,
        long? departmentId = null,
        string? verificationStatus = null,
        string? search = null)
    {
        var q =
            from qualification in context.Set<StudentQualification>().AsNoTracking()
            join user in context.Users.AsNoTracking() on qualification.UserId equals user.Id
            select new
            {
                Qualification = qualification,
                User = user,
                DepartmentId = user.Major != null ? user.Major.DepartmentId : (user.DepartmentId ?? 0)
            };

        if (qualificationId.HasValue)
            q = q.Where(x => x.Qualification.Id == qualificationId.Value);
        if (userId.HasValue)
            q = q.Where(x => x.Qualification.UserId == userId.Value);
        if (!string.IsNullOrWhiteSpace(qualificationType))
            q = q.Where(x => x.Qualification.QualificationType == qualificationType);
        if (organizationId.HasValue)
            q = q.Where(x => x.Qualification.OrganizationId == organizationId.Value);
        if (departmentId.HasValue)
            q = q.Where(x => x.DepartmentId == departmentId.Value);
        if (!string.IsNullOrWhiteSpace(verificationStatus))
            q = q.Where(x => x.Qualification.VerificationStatus == verificationStatus);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim();
            q = q.Where(x => x.User.FullName.Contains(value)
                || (x.User.StudentCode != null && x.User.StudentCode.Contains(value)));
        }

        return q.Select(x => new StudentQualificationModel(
            x.Qualification.Id,
            x.Qualification.UserId,
            x.User.FullName,
            x.User.StudentCode,
            x.Qualification.OrganizationId,
            x.DepartmentId,
            x.User.MajorId,
            x.Qualification.QualificationType,
            x.Qualification.TrainingStatus,
            x.Qualification.VerificationStatus,
            x.Qualification.CertificateNumber,
            x.Qualification.CertificateFileId,
            x.Qualification.IssuedAt,
            x.Qualification.ExpiresAt,
            x.Qualification.VerifiedBy,
            x.Qualification.VerifiedAt,
            x.Qualification.RejectionReason,
            x.Qualification.CreatedAt,
            x.Qualification.UpdatedAt));
    }

    public Task<StudentQualificationModel?> GetForUserAsync(
        long userId,
        string qualificationType,
        CancellationToken cancellationToken = default) =>
        Query(userId: userId, qualificationType: qualificationType)
            .SingleOrDefaultAsync(cancellationToken);

    public Task<StudentQualificationModel?> GetAsync(
        long qualificationId,
        CancellationToken cancellationToken = default) =>
        Query(qualificationId: qualificationId).SingleOrDefaultAsync(cancellationToken);

    public async Task<PagedResult<StudentQualificationModel>> SearchVerificationQueueAsync(
        long organizationId,
        long departmentId,
        string? verificationStatus,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query =
            from qualification in context.Set<StudentQualification>().AsNoTracking()
            join user in context.Users.AsNoTracking() on qualification.UserId equals user.Id
            select new
            {
                Qualification = qualification,
                User = user,
                DepartmentId = user.Major != null ? user.Major.DepartmentId : (user.DepartmentId ?? 0)
            };

        query = query.Where(x => x.Qualification.OrganizationId == organizationId
            && x.DepartmentId == departmentId);
        if (!string.IsNullOrWhiteSpace(verificationStatus))
            query = query.Where(x => x.Qualification.VerificationStatus == verificationStatus);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim();
            query = query.Where(x => x.User.FullName.Contains(value)
                || (x.User.StudentCode != null && x.User.StudentCode.Contains(value)));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.Qualification.VerificationStatus)
            .ThenByDescending(x => x.Qualification.UpdatedAt)
            .ThenBy(x => x.Qualification.UserId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new StudentQualificationModel(
                x.Qualification.Id,
                x.Qualification.UserId,
                x.User.FullName,
                x.User.StudentCode,
                x.Qualification.OrganizationId,
                x.DepartmentId,
                x.User.MajorId,
                x.Qualification.QualificationType,
                x.Qualification.TrainingStatus,
                x.Qualification.VerificationStatus,
                x.Qualification.CertificateNumber,
                x.Qualification.CertificateFileId,
                x.Qualification.IssuedAt,
                x.Qualification.ExpiresAt,
                x.Qualification.VerifiedBy,
                x.Qualification.VerifiedAt,
                x.Qualification.RejectionReason,
                x.Qualification.CreatedAt,
                x.Qualification.UpdatedAt))
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

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("The qualification changed concurrently. Reload and retry.");
        }
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

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("The qualification changed concurrently. Reload and retry.");
        }
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
